using RentifyxAiServices.SharedKernel.Events;

namespace RentifyxAiServices.Enrichment.Publishing;

public interface IEnrichmentEventPublisher
{
    Task PublishAsync(AssetEnrichmentSuggested suggestedEvent, CancellationToken cancellationToken = default);
}
