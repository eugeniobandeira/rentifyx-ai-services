namespace RentifyxAiServices.Enrichment.Bedrock;

public interface IBedrockEnrichmentClient
{
    Task<EnrichmentResult> GenerateAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}
