namespace RentifyxAiServices.SharedKernel.Events;

/// <summary>
/// Published when Dedupe finds an uploaded asset image whose perceptual hash exactly matches an
/// already-seen image belonging to a different asset - a signal of a reused/stolen photo, not a
/// hard verdict. asset-registry-api decides what to do with it (flag for manual review, etc.).
/// </summary>
public sealed record AssetDuplicateDetected(
    Guid AssetId,
    Guid DuplicateOfAssetId,
    string ImageHash,
    DateTimeOffset Timestamp,
    int SchemaVersion = 1);
