namespace RentifyxAiServices.Moderation.Publishing;

public interface IModerationEventPublisher
{
    Task PublishAsync(AssetMediaModerated moderatedEvent, CancellationToken cancellationToken = default);

    Task PublishAsync(AssetPendingManualReview pendingReviewEvent, CancellationToken cancellationToken = default);
}
