namespace RentifyxAiServices.SharedKernel.Events;

public sealed record AssetMediaModerated(
    Guid AssetId,
    Verdict Verdict,
    IReadOnlyList<ModerationLabel> Labels,
    float TopConfidence,
    DateTimeOffset Timestamp,
    string Bucket,
    string Key,
    int SchemaVersion = 2);
