using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RentifyxAiServices.Dedupe.Tests;

public class DedupeServiceTests
{
    private readonly Mock<IKeyConventionFilter> _keyFilter = new();
    private readonly Mock<IIdempotencyStore> _idempotencyStore = new();
    private readonly Mock<IAmazonS3> _s3Client = new();
    private readonly Mock<IPerceptualHashCalculator> _hashCalculator = new();
    private readonly Mock<IImageHashStore> _hashStore = new();
    private readonly Mock<IDedupeEventPublisher> _eventPublisher = new();
    private readonly Mock<IAmazonSQS> _sqsClient = new();

    private static readonly Guid AssetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherAssetId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Key = "assets/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/photo.jpg";
    private const string ImageHash = "abcdef0123456789";

    private DedupeService CreateService() => new(
        _keyFilter.Object,
        _idempotencyStore.Object,
        _s3Client.Object,
        _hashCalculator.Object,
        _hashStore.Object,
        _eventPublisher.Object,
        _sqsClient.Object,
        "https://sqs.test/failure-dlq",
        NullLogger<DedupeService>.Instance);

    private static S3Event.S3EventNotificationRecord CreateRecord(string key = Key, string eTag = "etag-1") => new()
    {
        S3 = new S3Event.S3Entity
        {
            Bucket = new S3Event.S3BucketEntity { Name = "media-bucket" },
            Object = new S3Event.S3ObjectEntity { Key = key, ETag = eTag }
        }
    };

    private void SetUpHappyPathUpToHash()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(true);
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _s3Client
            .Setup(s => s.GetObjectAsync("media-bucket", Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream([1, 2, 3]) });
        _hashCalculator.Setup(h => h.ComputeHash(It.IsAny<Stream>())).Returns(ImageHash);
    }

    [Fact]
    public async Task ProcessAsync_KeyDoesNotMatchConvention_SkipsWithoutDownstreamCalls()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(false);

        await CreateService().ProcessAsync(CreateRecord());

        _idempotencyStore.Verify(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        _s3Client.Verify(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEtag_SkipsWithoutS3Call()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(true);
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await CreateService().ProcessAsync(CreateRecord());

        _s3Client.Verify(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_S3Fails_SendsToDlqWithoutPublishingOrRecording()
    {
        _keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(true);
        _idempotencyStore.Setup(s => s.TryMarkProcessedAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _s3Client
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonS3Exception("not found"));

        await CreateService().ProcessAsync(CreateRecord());

        _sqsClient.Verify(
            s => s.SendMessageAsync(It.Is<SendMessageRequest>(r => r.QueueUrl == "https://sqs.test/failure-dlq"), It.IsAny<CancellationToken>()),
            Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetDuplicateDetected>(), It.IsAny<CancellationToken>()), Times.Never);
        _hashStore.Verify(h => h.RecordAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_NewHash_RecordsItWithoutPublishing()
    {
        SetUpHappyPathUpToHash();
        _hashStore.Setup(h => h.FindExistingAssetIdAsync(ImageHash, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        await CreateService().ProcessAsync(CreateRecord());

        _hashStore.Verify(h => h.RecordAsync(ImageHash, AssetId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetDuplicateDetected>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_HashMatchesDifferentAsset_PublishesDuplicateWithoutRecording()
    {
        SetUpHappyPathUpToHash();
        _hashStore.Setup(h => h.FindExistingAssetIdAsync(ImageHash, It.IsAny<CancellationToken>())).ReturnsAsync(OtherAssetId);

        await CreateService().ProcessAsync(CreateRecord());

        _eventPublisher.Verify(
            p => p.PublishAsync(
                It.Is<AssetDuplicateDetected>(e => e.AssetId == AssetId && e.DuplicateOfAssetId == OtherAssetId && e.ImageHash == ImageHash),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _hashStore.Verify(h => h.RecordAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessAsync_HashMatchesSameAsset_TreatsAsNewAndDoesNotPublish()
    {
        SetUpHappyPathUpToHash();
        _hashStore.Setup(h => h.FindExistingAssetIdAsync(ImageHash, It.IsAny<CancellationToken>())).ReturnsAsync(AssetId);

        await CreateService().ProcessAsync(CreateRecord());

        _eventPublisher.Verify(p => p.PublishAsync(It.IsAny<AssetDuplicateDetected>(), It.IsAny<CancellationToken>()), Times.Never);
        _hashStore.Verify(h => h.RecordAsync(ImageHash, AssetId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
