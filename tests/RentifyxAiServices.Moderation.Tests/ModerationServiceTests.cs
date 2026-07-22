using Amazon.Lambda.S3Events;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentifyxAiServices.Moderation;
using RentifyxAiServices.SharedLibrary.Events;
using RentifyxAiServices.SharedLibrary.Idempotency;
using Xunit;

namespace RentifyxAiServices.Moderation.Tests;

public class ModerationServiceTests
{
    private readonly Mock<IKeyConventionFilter> _keyFilter = new();
    private readonly Mock<IIdempotencyStore> _idempotencyStore = new();
    private readonly Mock<IRekognitionModerationClient> _rekognitionClient = new();
    private readonly Mock<IThresholdEvaluator> _thresholdEvaluator = new();
    private readonly Mock<IModerationEventPublisher> _eventPublisher = new();
    private readonly Mock<IAmazonSQS> _sqsClient = new();

    private static readonly Guid AssetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Key = "assets/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/photo.jpg";

    private ModerationService CreateService() => new(
        _keyFilter.Object,
        _idempotencyStore.Object,
        _rekognitionClient.Object,
        _thresholdEvaluator.Object,
        _eventPublisher.Object,
        _sqsClient.Object,
        "https://sqs.test/failure-dlq",
        NullLogger<ModerationService>.Instance);

    private static S3Event.S3EventNotificationRecord CreateRecord(string key = Key, string eTag = "etag-1") => new()
    {
        S3 = new S3Event.S3Entity
        {
            Bucket = new S3Event.S3BucketEntity { Name = "media-bucket" },
            Object = new S3Event.S3ObjectEntity { Key = key, ETag = eTag }
        }
    };

    [Fact]
    public async Task ProcessAsync_KeyDoesNotMatchConvention_SkipsWithoutDownstreamCalls()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(false);

        await CreateService().ProcessAsync(CreateRecord());

        _idempotencyStore.Verify(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        _rekognitionClient.Verify(r => r.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEtag_SkipsWithoutRekognitionCall()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(true);
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await CreateService().ProcessAsync(CreateRecord());

        _rekognitionClient.Verify(r => r.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_RekognitionFailsAfterRetries_SendsToDlqWithoutPublishing()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(true);
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _rekognitionClient
            .Setup(r => r.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationScanResult([], Succeeded: false, FailureReason: "throttled"));

        await CreateService().ProcessAsync(CreateRecord());

        _sqsClient.Verify(
            s => s.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.test/failure-dlq"), It.IsAny<CancellationToken>()),
            Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetMediaModerated>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetPendingManualReview>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(Verdict.Approved)]
    [InlineData(Verdict.Rejected)]
    public async Task ProcessAsync_ApprovedOrRejected_PublishesAssetMediaModeratedOnly(Verdict verdict)
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(true);
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _rekognitionClient
            .Setup(r => r.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationScanResult([new ModerationLabel("Explicit Nudity", 95f)], Succeeded: true, FailureReason: null));
        _thresholdEvaluator.Setup(e => e.Evaluate(It.IsAny<IReadOnlyList<ModerationLabel>>())).Returns(verdict);

        await CreateService().ProcessAsync(CreateRecord());

        _eventPublisher.Verify(p => p.PublishAsync(It.Is<AssetMediaModerated>(e => e.AssetId == AssetId && e.Verdict == verdict), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetPendingManualReview>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_PendingReview_PublishesAssetPendingManualReviewOnly()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(true);
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _rekognitionClient
            .Setup(r => r.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationScanResult([new ModerationLabel("Explicit Nudity", 75f)], Succeeded: true, FailureReason: null));
        _thresholdEvaluator.Setup(e => e.Evaluate(It.IsAny<IReadOnlyList<ModerationLabel>>())).Returns(Verdict.PendingReview);

        await CreateService().ProcessAsync(CreateRecord());

        _eventPublisher.Verify(p => p.PublishAsync(It.Is<AssetPendingManualReview>(e => e.AssetId == AssetId), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetMediaModerated>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
