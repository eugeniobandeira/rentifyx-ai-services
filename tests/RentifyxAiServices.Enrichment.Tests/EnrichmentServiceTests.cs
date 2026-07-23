using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentifyxAiServices.Enrichment.Bedrock;
using RentifyxAiServices.Enrichment.Publishing;
using RentifyxAiServices.SharedKernel.Events;
using RentifyxAiServices.SharedKernel.Idempotency;
using Xunit;

namespace RentifyxAiServices.Enrichment.Tests;

public class EnrichmentServiceTests
{
    private readonly Mock<IIdempotencyStore> _idempotencyStore = new();
    private readonly Mock<IAmazonS3> _s3Client = new();
    private readonly Mock<IBedrockEnrichmentClient> _bedrockClient = new();
    private readonly Mock<IEnrichmentEventPublisher> _eventPublisher = new();
    private readonly Mock<IAmazonSQS> _sqsClient = new();

    private static readonly Guid AssetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private EnrichmentService CreateService() => new(
        _idempotencyStore.Object,
        _s3Client.Object,
        _bedrockClient.Object,
        _eventPublisher.Object,
        _sqsClient.Object,
        "https://sqs.test/enrichment-failure-dlq",
        NullLogger<EnrichmentService>.Instance);

    private static AssetMediaModerated CreateEvent(Verdict verdict = Verdict.Approved) => new(
        AssetId, verdict, [], 10f, DateTimeOffset.UtcNow, "media-bucket", "assets/owner/asset/photo.jpg");

    [Fact]
    public async Task ProcessAsync_NotApproved_SkipsWithoutDownstreamCalls()
    {
        await CreateService().ProcessAsync(CreateEvent(Verdict.Rejected));

        _idempotencyStore.Verify(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        _s3Client.Verify(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _bedrockClient.Verify(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_AlreadyEnriched_SkipsWithoutS3OrBedrockCall()
    {
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await CreateService().ProcessAsync(CreateEvent());

        _s3Client.Verify(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _bedrockClient.Verify(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_S3ObjectMissing_SendsToDlqWithoutPublishing()
    {
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("not found") { StatusCode = HttpStatusCode.NotFound });

        await CreateService().ProcessAsync(CreateEvent());

        _bedrockClient.Verify(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        _sqsClient.Verify(
            s => s.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.test/enrichment-failure-dlq"), It.IsAny<CancellationToken>()),
            Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetEnrichmentSuggested>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_BedrockFails_SendsToDlqWithoutPublishing()
    {
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream([1, 2, 3]) });
        _bedrockClient
            .Setup(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentResult(null, [], false, "throttled"));

        await CreateService().ProcessAsync(CreateEvent());

        _sqsClient.Verify(
            s => s.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.test/enrichment-failure-dlq"), It.IsAny<CancellationToken>()),
            Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetEnrichmentSuggested>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_Success_PublishesAssetEnrichmentSuggested()
    {
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream([1, 2, 3]) });
        _bedrockClient
            .Setup(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EnrichmentResult("A cozy apartment", ["balcony"], true, null));

        await CreateService().ProcessAsync(CreateEvent());

        _eventPublisher.Verify(
            p => p.PublishAsync(It.Is<AssetEnrichmentSuggested>(e => e.AssetId == AssetId && e.Description == "A cozy apartment"), It.IsAny<CancellationToken>()),
            Times.Once);
        _sqsClient.Verify(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
