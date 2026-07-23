using Moq;
using RentifyxAiServices.Enrichment.Publishing;
using RentifyxAiServices.SharedKernel.Events;
using RentifyxAiServices.SharedKernel.Kafka;
using Xunit;

namespace RentifyxAiServices.Enrichment.Tests;

public class KafkaEnrichmentEventPublisherTests
{
    private readonly Mock<IEventPublisher<AssetEnrichmentSuggested>> _suggestedPublisher = new();

    private KafkaEnrichmentEventPublisher CreatePublisher() => new(_suggestedPublisher.Object);

    [Fact]
    public async Task PublishAsync_AssetEnrichmentSuggested_DelegatesToKafkaPublisher()
    {
        AssetEnrichmentSuggested @event = new(Guid.NewGuid(), "A cozy loft apartment", ["cozy", "loft"], DateTimeOffset.UtcNow);

        await CreatePublisher().PublishAsync(@event);

        _suggestedPublisher.Verify(p => p.PublishAsync(@event.AssetId.ToString(), @event, It.IsAny<CancellationToken>()), Times.Once);
    }
}
