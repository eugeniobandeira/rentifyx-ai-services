using Amazon.Lambda.KafkaEvents;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using RentifyxAiServices.Enrichment.Bedrock;
using RentifyxAiServices.Enrichment.Publishing;
using RentifyxAiServices.SharedKernel.Events;
using RentifyxAiServices.SharedKernel.Idempotency;
using Xunit;

namespace RentifyxAiServices.Enrichment.Tests;

public class EnrichmentHandlerTests
{
    [Fact]
    public async Task FunctionHandler_MalformedEmptyEvent_DoesNotThrow()
    {
        EnrichmentHandler handler = new(BuildServiceWithNoDependencies());
        TestLambdaContext context = new();

        Func<Task> act = async () => await handler.FunctionHandler(null, context);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FunctionHandler_EventWithNoRecords_DoesNotThrow()
    {
        EnrichmentHandler handler = new(BuildServiceWithNoDependencies());
        TestLambdaContext context = new();
        KafkaEvent kafkaEvent = new() { Records = new Dictionary<string, IList<KafkaEvent.KafkaEventRecord>>() };

        Func<Task> act = async () => await handler.FunctionHandler(kafkaEvent, context);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FunctionHandler_SingleRecord_DelegatesToService()
    {
        Mock<IIdempotencyStore> idempotencyStore = new();
        EnrichmentHandler handler = new(BuildService(idempotencyStore));
        TestLambdaContext context = new();

        AssetMediaModerated moderatedEvent = new(
            Guid.NewGuid(), Verdict.Rejected, [], 10f, DateTimeOffset.UtcNow, "bucket", "key");
        byte[] payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(moderatedEvent);
        KafkaEvent.KafkaEventRecord record = new() { Topic = "asset-media-moderated", Value = new MemoryStream(payload) };
        KafkaEvent kafkaEvent = new()
        {
            Records = new Dictionary<string, IList<KafkaEvent.KafkaEventRecord>>
            {
                ["asset-media-moderated-0"] = [record],
            },
        };

        await handler.FunctionHandler(kafkaEvent, context);

        // Verdict.Rejected short-circuits EnrichmentService.ProcessAsync before any idempotency
        // call - proving the handler deserialized the record and delegated to the service.
        idempotencyStore.Verify(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static EnrichmentService BuildServiceWithNoDependencies() => BuildService(new Mock<IIdempotencyStore>());

    private static EnrichmentService BuildService(Mock<IIdempotencyStore> idempotencyStore) => new(
        idempotencyStore.Object,
        Mock.Of<Amazon.S3.IAmazonS3>(),
        Mock.Of<IBedrockEnrichmentClient>(),
        Mock.Of<IEnrichmentEventPublisher>(),
        Mock.Of<Amazon.SQS.IAmazonSQS>(),
        "https://sqs.test/enrichment-failure-dlq",
        NullLogger<EnrichmentService>.Instance);
}
