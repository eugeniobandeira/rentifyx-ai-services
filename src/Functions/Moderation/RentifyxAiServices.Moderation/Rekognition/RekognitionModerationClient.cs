using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Microsoft.Extensions.Logging;
using SharedModerationLabel = RentifyxAiServices.SharedKernel.Events.ModerationLabel;

namespace RentifyxAiServices.Moderation.Rekognition;

public sealed class RekognitionModerationClient(
    IAmazonRekognition rekognitionClient,
    ILogger<RekognitionModerationClient> logger) : IRekognitionModerationClient
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(200);

    public async Task<ModerationScanResult> ScanAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        DetectModerationLabelsRequest request = new()
        {
            Image = new Image { S3Object = new S3Object { Bucket = bucket, Name = key } }
        };

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                DetectModerationLabelsResponse response = await rekognitionClient
                    .DetectModerationLabelsAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<SharedModerationLabel> labels = response.ModerationLabels
                    .Select(l => new SharedModerationLabel(l.Name, l.Confidence ?? 0f))
                    .ToList();

                return new ModerationScanResult(labels, Succeeded: true, FailureReason: null);
            }
            catch (Exception ex) when (IsThrottling(ex) && attempt < MaxRetries)
            {
                TimeSpan delay = InitialBackoff * (1 << (attempt - 1));
                logger.LogWarning(ex, "Rekognition throttled on attempt {Attempt}, retrying in {Delay}", attempt, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsThrottling(ex))
            {
                logger.LogError(ex, "Rekognition throttled after {MaxRetries} attempts", MaxRetries);
                return new ModerationScanResult([], Succeeded: false, FailureReason: "Throttled after retries exhausted");
            }
            catch (AmazonRekognitionException ex)
            {
                logger.LogError(ex, "Rekognition rejected the image, not retrying");
                return new ModerationScanResult([], Succeeded: false, FailureReason: ex.Message);
            }
        }

        return new ModerationScanResult([], Succeeded: false, FailureReason: "Retries exhausted");
    }

    private static bool IsThrottling(Exception ex) =>
        ex is ProvisionedThroughputExceededException or ThrottlingException;
}
