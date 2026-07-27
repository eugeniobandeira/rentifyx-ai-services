namespace RentifyxAiServices.Dedupe.Publishing;

public interface IDedupeEventPublisher
{
    Task PublishAsync(AssetDuplicateDetected duplicateEvent, CancellationToken cancellationToken = default);
}
