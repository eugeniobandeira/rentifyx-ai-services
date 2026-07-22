using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using RentifyxAiServices.SharedLibrary.Events;
using RentifyxAiServices.SharedLibrary.Kafka;

namespace RentifyxAiServices.Moderation;

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
