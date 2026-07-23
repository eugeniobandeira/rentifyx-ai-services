namespace RentifyxAiServices.SharedKernel.Events;

public sealed record AssetEnrichmentSuggested(
    Guid AssetId,
    string Description,
    IReadOnlyList<string> Tags,
    DateTimeOffset Timestamp,
    int SchemaVersion = 1);
