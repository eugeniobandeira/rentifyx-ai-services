namespace RentifyxAiServices.Moderation.Rekognition;

public interface IRekognitionModerationClient
{
    Task<ModerationScanResult> ScanAsync(string bucket, string key, CancellationToken cancellationToken = default);
}
