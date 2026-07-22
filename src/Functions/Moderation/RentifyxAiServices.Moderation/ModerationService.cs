using Amazon.Lambda.S3Events;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using RentifyxAiServices.SharedLibrary.Events;
using RentifyxAiServices.SharedLibrary.Idempotency;

namespace RentifyxAiServices.Moderation;

public sealed class ModerationService(
    IKeyConventionFilter keyFilter,
    IIdempotencyStore idempotencyStore,
    IRekognitionModerationClient rekognitionClient,
    IThresholdEvaluator thresholdEvaluator,
    IModerationEventPublisher eventPublisher,
    IAmazonSQS sqsClient,
    string failureDlqUrl,
    ILogger<ModerationService> logger)
{
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromDays(7);

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

        ModerationScanResult scanResult = await rekognitionClient.ScanAsync(bucket, key, cancellationToken).ConfigureAwait(false);
        if (!scanResult.Succeeded)
        {
            await SendToFailureDlqAsync(bucket, key, scanResult.FailureReason, cancellationToken).ConfigureAwait(false);
            return;
        }

        Verdict verdict = thresholdEvaluator.Evaluate(scanResult.Labels);
        Guid assetId = ExtractAssetId(key);
        float topConfidence = scanResult.Labels.Count > 0 ? scanResult.Labels.Max(l => l.Confidence) : 0f;
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        if (verdict == Verdict.PendingReview)
        {
            AssetPendingManualReview pendingReviewEvent = new(assetId, scanResult.Labels, topConfidence, timestamp);
            await eventPublisher.PublishAsync(pendingReviewEvent, cancellationToken).ConfigureAwait(false);
            return;
        }

        AssetMediaModerated moderatedEvent = new(assetId, verdict, scanResult.Labels, topConfidence, timestamp);
        await eventPublisher.PublishAsync(moderatedEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendToFailureDlqAsync(string bucket, string key, string? failureReason, CancellationToken cancellationToken)
    {
        logger.LogError("Rekognition scan failed for {Bucket}/{Key}: {Reason}", bucket, key, failureReason);

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
