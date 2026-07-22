namespace RentifyxAiServices.Moderation.Rekognition;

public sealed record ModerationScanResult(
    IReadOnlyList<ModerationLabel> Labels,
    bool Succeeded,
    string? FailureReason);
