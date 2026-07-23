using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using RentifyxAiServices.Enrichment.Bedrock;
using RentifyxAiServices.Enrichment.Publishing;
using RentifyxAiServices.SharedKernel.Events;
using RentifyxAiServices.SharedKernel.Idempotency;

namespace RentifyxAiServices.Enrichment;

public sealed class EnrichmentService(
    IIdempotencyStore idempotencyStore,
    IAmazonS3 s3Client,
    IBedrockEnrichmentClient bedrockClient,
    IEnrichmentEventPublisher eventPublisher,
    IAmazonSQS sqsClient,
    string failureDlqUrl,
    ILogger<EnrichmentService> logger)
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromDays(7);

    public async Task ProcessAsync(AssetMediaModerated moderatedEvent, CancellationToken cancellationToken = default)
    {
        if (moderatedEvent.Verdict != Verdict.Approved)
        {
            logger.LogInformation("Skipping asset {AssetId}: verdict is {Verdict}, only Approved assets are enriched", moderatedEvent.AssetId, moderatedEvent.Verdict);
            return;
        }

        string idempotencyKey = $"enrichment:{moderatedEvent.AssetId}";
        bool claimed = await idempotencyStore.TryMarkProcessedAsync(idempotencyKey, IdempotencyTtl, cancellationToken).ConfigureAwait(false);
        if (!claimed)
        {
            logger.LogInformation("Skipping asset {AssetId}: already enriched", moderatedEvent.AssetId);
            return;
        }

        byte[]? imageBytes = await TryFetchImageAsync(moderatedEvent.Bucket, moderatedEvent.Key, cancellationToken).ConfigureAwait(false);
        if (imageBytes is null)
        {
            await SendToFailureDlqAsync(moderatedEvent.AssetId, "S3 object not found").ConfigureAwait(false);
            return;
        }

        EnrichmentResult result = await bedrockClient.GenerateAsync(imageBytes, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await SendToFailureDlqAsync(moderatedEvent.AssetId, result.FailureReason).ConfigureAwait(false);
            return;
        }

        AssetEnrichmentSuggested suggestedEvent = new(moderatedEvent.AssetId, result.Description!, result.Tags, DateTimeOffset.UtcNow);
        await eventPublisher.PublishAsync(suggestedEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]?> TryFetchImageAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        try
        {
            using GetObjectResponse response = await s3Client.GetObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);
            using MemoryStream buffer = new();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogError(ex, "S3 object {Bucket}/{Key} not found", bucket, key);
            return null;
        }
    }

    private async Task SendToFailureDlqAsync(Guid assetId, string? failureReason)
    {
        logger.LogError("Enrichment failed for asset {AssetId}: {Reason}", assetId, failureReason);

        SendMessageRequest dlqRequest = new()
        {
            QueueUrl = failureDlqUrl,
            MessageBody = System.Text.Json.JsonSerializer.Serialize(new { AssetId = assetId, FailureReason = failureReason }),
        };

        await sqsClient.SendMessageAsync(dlqRequest).ConfigureAwait(false);
    }
}
