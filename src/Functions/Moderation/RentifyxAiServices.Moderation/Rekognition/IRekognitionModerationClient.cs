namespace RentifyxAiServices.Moderation;

public interface IRekognitionModerationClient
{
    Task<ModerationScanResult> ScanAsync(string bucket, string key, CancellationToken cancellationToken = default);
}
