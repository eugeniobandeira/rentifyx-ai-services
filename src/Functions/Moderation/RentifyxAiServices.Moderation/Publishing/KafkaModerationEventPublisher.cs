using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;

namespace RentifyxAiServices.Moderation.Publishing;

public sealed class KafkaModerationEventPublisher(
    IEventPublisher<AssetMediaModerated> moderatedPublisher,
    IEventPublisher<AssetPendingManualReview> pendingReviewPublisher,
    IAmazonSQS sqsClient,
    string reviewQueueUrl) : IModerationEventPublisher
{
    public Task PublishAsync(AssetMediaModerated moderatedEvent, CancellationToken cancellationToken = default) =>
        moderatedPublisher.PublishAsync(moderatedEvent.AssetId.ToString(), moderatedEvent, cancellationToken);

    public async Task PublishAsync(AssetPendingManualReview pendingReviewEvent, CancellationToken cancellationToken = default)
    {
        await pendingReviewPublisher.PublishAsync(pendingReviewEvent.AssetId.ToString(), pendingReviewEvent, cancellationToken).ConfigureAwait(false);

        SendMessageRequest sqsRequest = new()
        {
            QueueUrl = reviewQueueUrl,
            MessageBody = JsonSerializer.Serialize(pendingReviewEvent)
        };

        await sqsClient.SendMessageAsync(sqsRequest, cancellationToken).ConfigureAwait(false);
    }
}
