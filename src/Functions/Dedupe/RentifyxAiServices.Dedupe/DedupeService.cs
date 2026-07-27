using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;

namespace RentifyxAiServices.Dedupe;

public sealed class DedupeService(
    IKeyConventionFilter keyFilter,
    IIdempotencyStore idempotencyStore,
    IAmazonS3 s3Client,
    IPerceptualHashCalculator hashCalculator,
    IImageHashStore hashStore,
    IDedupeEventPublisher eventPublisher,
    IAmazonSQS sqsClient,
    string failureDlqUrl,
    ILogger<DedupeService> logger)
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan HashTtl = TimeSpan.FromDays(365);

    public async Task ProcessAsync(S3Event.S3EventNotificationRecord record, CancellationToken cancellationToken = default)
    {
        string bucket = record.S3.Bucket.Name;
        string key = record.S3.Object.Key;
        string eTag = record.S3.Object.ETag;

        if (!keyFilter.Matches(key))
        {
            logger.LogInformation("Skipping key {Key}: does not match asset convention", key);
            return;
        }

        string idempotencyKey = $"{bucket}/{key}#{eTag}";
        bool claimed = await idempotencyStore.TryMarkProcessedAsync(idempotencyKey, IdempotencyTtl, cancellationToken).ConfigureAwait(false);
        if (!claimed)
        {
            logger.LogInformation("Skipping key {Key}: already processed (ETag {ETag})", key, eTag);
            return;
        }

        Guid assetId = ExtractAssetId(key);
        string imageHash;

        try
        {
            using GetObjectResponse s3Object = await s3Client.GetObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);
            imageHash = hashCalculator.ComputeHash(s3Object.ResponseStream);
        }
        catch (Exception ex) when (ex is AmazonS3Exception or SixLabors.ImageSharp.UnknownImageFormatException)
        {
            await SendToFailureDlqAsync(bucket, key, ex.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        Guid? existingAssetId = await hashStore.FindExistingAssetIdAsync(imageHash, cancellationToken).ConfigureAwait(false);

        if (existingAssetId is { } duplicateOfAssetId && duplicateOfAssetId != assetId)
        {
            logger.LogWarning(
                "Duplicate image detected. AssetId={AssetId} DuplicateOfAssetId={DuplicateOfAssetId} ImageHash={ImageHash}",
                assetId, duplicateOfAssetId, imageHash);

            AssetDuplicateDetected duplicateEvent = new(assetId, duplicateOfAssetId, imageHash, DateTimeOffset.UtcNow);
            await eventPublisher.PublishAsync(duplicateEvent, cancellationToken).ConfigureAwait(false);
            return;
        }

        await hashStore.RecordAsync(imageHash, assetId, HashTtl, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendToFailureDlqAsync(string bucket, string key, string? failureReason, CancellationToken cancellationToken)
    {
        logger.LogError("Dedupe hash computation failed for {Bucket}/{Key}: {Reason}", bucket, key, failureReason);

        SendMessageRequest dlqRequest = new()
        {
            QueueUrl = failureDlqUrl,
            MessageBody = System.Text.Json.JsonSerializer.Serialize(new { Bucket = bucket, Key = key, FailureReason = failureReason })
        };

        await sqsClient.SendMessageAsync(dlqRequest, cancellationToken).ConfigureAwait(false);
    }

    private static Guid ExtractAssetId(string key)
    {
        string[] segments = key.Split('/');
        return Guid.Parse(segments[2]);
    }
}
