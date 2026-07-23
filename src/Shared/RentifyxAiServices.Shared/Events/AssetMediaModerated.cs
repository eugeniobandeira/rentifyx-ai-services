namespace RentifyxAiServices.SharedKernel.Events;

public sealed record AssetMediaModerated(
    Guid AssetId,
    Verdict Verdict,
    IReadOnlyList<ModerationLabel> Labels,
    float TopConfidence,
    DateTimeOffset Timestamp,
    int SchemaVersion = 1);
