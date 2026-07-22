namespace RentifyxAiServices.SharedLibrary.Events;

public sealed record AssetPendingManualReview(
    Guid AssetId,
    IReadOnlyList<ModerationLabel> Labels,
    float TopConfidence,
    DateTimeOffset Timestamp,
    int SchemaVersion = 1);
