using Amazon.SQS;
using Amazon.SQS.Model;
using Moq;
using Xunit;

namespace RentifyxAiServices.Moderation.Tests;

public class KafkaModerationEventPublisherTests
{
    private readonly Mock<IEventPublisher<AssetMediaModerated>> _moderatedPublisher = new();
    private readonly Mock<IEventPublisher<AssetPendingManualReview>> _pendingReviewPublisher = new();
    private readonly Mock<IAmazonSQS> _sqsClient = new();

    private KafkaModerationEventPublisher CreatePublisher() =>
        new(_moderatedPublisher.Object, _pendingReviewPublisher.Object, _sqsClient.Object, "https://sqs.test/review-queue");

    [Fact]
    public async Task PublishAsync_AssetMediaModerated_PublishesToKafkaOnly()
    {
        AssetMediaModerated @event = new(Guid.NewGuid(), Verdict.Approved, [], 10f, DateTimeOffset.UtcNow);

        await CreatePublisher().PublishAsync(@event);

        _moderatedPublisher.Verify(p => p.PublishAsync(@event.AssetId.ToString(), @event, It.IsAny<CancellationToken>()), Times.Once);
        _sqsClient.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_AssetPendingManualReview_PublishesToKafkaAndEnqueuesSqs()
    {
        AssetPendingManualReview @event = new(Guid.NewGuid(), [], 75f, DateTimeOffset.UtcNow);

        await CreatePublisher().PublishAsync(@event);

        _pendingReviewPublisher.Verify(p => p.PublishAsync(@event.AssetId.ToString(), @event, It.IsAny<CancellationToken>()), Times.Once);
        _sqsClient.Verify(
            s => s.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.test/review-queue"), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
