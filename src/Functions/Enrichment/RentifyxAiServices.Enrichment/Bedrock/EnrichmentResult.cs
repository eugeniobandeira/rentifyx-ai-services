namespace RentifyxAiServices.Enrichment.Bedrock;

public sealed record EnrichmentResult(
    string? Description,
    IReadOnlyList<string> Tags,
    bool Succeeded,
    string? FailureReason);
