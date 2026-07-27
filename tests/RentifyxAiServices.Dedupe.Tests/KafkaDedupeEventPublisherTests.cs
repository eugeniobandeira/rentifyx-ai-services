using Moq;
using Xunit;

namespace RentifyxAiServices.Dedupe.Tests;

public class KafkaDedupeEventPublisherTests
{
    private readonly Mock<IEventPublisher<AssetDuplicateDetected>> _publisher = new();

    private KafkaDedupeEventPublisher CreatePublisher() => new(_publisher.Object);

    [Fact]
    public async Task PublishAsync_ForwardsToKafkaKeyedByAssetId()
    {
        AssetDuplicateDetected @event = new(Guid.NewGuid(), Guid.NewGuid(), "abc123", DateTimeOffset.UtcNow);

        await CreatePublisher().PublishAsync(@event);

        _publisher.Verify(p => p.PublishAsync(@event.AssetId.ToString(), @event, It.IsAny<CancellationToken>()), Times.Once);
    }
}
