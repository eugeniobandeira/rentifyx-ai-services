using RentifyxAiServices.SharedKernel.Events;
using RentifyxAiServices.SharedKernel.Kafka;

namespace RentifyxAiServices.Enrichment.Publishing;

public sealed class KafkaEnrichmentEventPublisher(IEventPublisher<AssetEnrichmentSuggested> suggestedPublisher) : IEnrichmentEventPublisher
{
    public Task PublishAsync(AssetEnrichmentSuggested suggestedEvent, CancellationToken cancellationToken = default) =>
        suggestedPublisher.PublishAsync(suggestedEvent.AssetId.ToString(), suggestedEvent, cancellationToken);
}
