namespace RentifyxAiServices.Dedupe.Publishing;

public sealed class KafkaDedupeEventPublisher(IEventPublisher<AssetDuplicateDetected> publisher) : IDedupeEventPublisher
{
    public Task PublishAsync(AssetDuplicateDetected duplicateEvent, CancellationToken cancellationToken = default)
        => publisher.PublishAsync(duplicateEvent.AssetId.ToString(), duplicateEvent, cancellationToken);
}
