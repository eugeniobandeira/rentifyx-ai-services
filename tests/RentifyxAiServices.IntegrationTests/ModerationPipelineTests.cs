using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentifyxAiServices.Moderation;
using RentifyxAiServices.SharedLibrary.Events;
using RentifyxAiServices.SharedLibrary.Idempotency;
using RentifyxAiServices.SharedLibrary.Kafka;
using Testcontainers.Kafka;
using Testcontainers.LocalStack;
using Xunit;

namespace RentifyxAiServices.IntegrationTests;

/// <summary>
/// E-02 MOD-01/02/03: S3 object -> Rekognition (stubbed, unsupported by LocalStack community) -> Kafka event,
/// against real LocalStack S3/DynamoDB and a real Kafka broker via Testcontainers. No AWS credentials used.
/// </summary>
public sealed class ModerationPipelineTests : IAsyncLifetime
{
    private const string Bucket = "media-bucket";
    private const string Table = "moderation-idempotency";
    private const string ModeratedTopic = "asset-media-moderated";
    private const string Key = "assets/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/photo.jpg";
    private static readonly Guid AssetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:3.7").Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.1").Build();

    private IAmazonS3 _s3 = null!;
    private IAmazonDynamoDB _dynamoDb = null!;
    private IProducer<string, string> _producer = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_localStack.StartAsync(), _kafka.StartAsync());

        Amazon.RegionEndpoint region = Amazon.RegionEndpoint.USEast1;
        Amazon.Runtime.BasicAWSCredentials credentials = new("test", "test");

        _s3 = new AmazonS3Client(credentials, new AmazonS3Config { ServiceURL = _localStack.GetConnectionString(), ForcePathStyle = true, RegionEndpoint = region });
        _dynamoDb = new AmazonDynamoDBClient(credentials, new AmazonDynamoDBConfig { ServiceURL = _localStack.GetConnectionString(), RegionEndpoint = region });

        await _s3.PutBucketAsync(Bucket);
        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = Table,
            AttributeDefinitions = [new AttributeDefinition("IdempotencyKey", ScalarAttributeType.S)],
            KeySchema = [new KeySchemaElement("IdempotencyKey", KeyType.HASH)],
            BillingMode = BillingMode.PAY_PER_REQUEST
        });

        _producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build();
    }

    public async Task DisposeAsync()
    {
        _producer.Dispose();
        await Task.WhenAll(_localStack.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    private ModerationService CreateService(IRekognitionModerationClient rekognitionClient) =>
        new(
            new AssetKeyConventionFilter(),
            new DynamoDbIdempotencyStore(_dynamoDb, Table),
            rekognitionClient,
            new ThresholdEvaluator(),
            new KafkaModerationEventPublisher(
                new KafkaEventPublisher<AssetMediaModerated>(_producer, ModeratedTopic),
                new KafkaEventPublisher<AssetPendingManualReview>(_producer, "asset-pending-manual-review"),
                Mock.Of<IAmazonSQS>(),
                "unused-review-queue"),
            Mock.Of<IAmazonSQS>(),
            "unused-failure-dlq",
            NullLogger<ModerationService>.Instance);

    private static S3Event.S3EventNotificationRecord CreateRecord(string eTag) => new()
    {
        S3 = new S3Event.S3Entity
        {
            Bucket = new S3Event.S3BucketEntity { Name = Bucket },
            Object = new S3Event.S3ObjectEntity { Key = Key, ETag = eTag }
        }
    };

    private static Mock<IRekognitionModerationClient> StubRekognition(bool succeeded, params ModerationLabel[] labels)
    {
        Mock<IRekognitionModerationClient> mock = new();
        mock.Setup(r => r.ScanAsync(Bucket, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModerationScanResult(labels, succeeded, succeeded ? null : "stubbed failure"));
        return mock;
    }

    [Fact]
    public async Task ProcessAsync_CleanImage_PublishesApprovedVerdictToKafka()
    {
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = Key, ContentBody = "fake-image-bytes" });
        Mock<IRekognitionModerationClient> rekognition = StubRekognition(succeeded: true);
        ModerationService service = CreateService(rekognition.Object);

        using IConsumer<string, string> consumer = BuildConsumer(ModeratedTopic);

        await service.ProcessAsync(CreateRecord("etag-clean"));

        ConsumeResult<string, string> result = consumer.Consume(TimeSpan.FromSeconds(15));
        result.Should().NotBeNull();
        result.Message.Value.Should().Contain(AssetId.ToString()).And.Contain("\"Approved\"");
    }

    [Fact]
    public async Task ProcessAsync_SameEtagTwice_SkipsSecondScan()
    {
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = Key, ContentBody = "fake-image-bytes" });
        Mock<IRekognitionModerationClient> rekognition = StubRekognition(succeeded: true);
        ModerationService service = CreateService(rekognition.Object);

        S3Event.S3EventNotificationRecord record = CreateRecord("etag-duplicate");
        await service.ProcessAsync(record);
        await service.ProcessAsync(record);

        rekognition.Verify(r => r.ScanAsync(Bucket, Key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_ViolatingImage_PublishesRejectedVerdictWithLabels()
    {
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = Key, ContentBody = "fake-image-bytes" });
        Mock<IRekognitionModerationClient> rekognition = StubRekognition(succeeded: true, new ModerationLabel("Explicit Nudity", 95f));
        ModerationService service = CreateService(rekognition.Object);

        using IConsumer<string, string> consumer = BuildConsumer(ModeratedTopic);

        await service.ProcessAsync(CreateRecord("etag-violating"));

        ConsumeResult<string, string> result = consumer.Consume(TimeSpan.FromSeconds(15));
        result.Should().NotBeNull();
        result.Message.Value.Should().Contain("\"Rejected\"").And.Contain("Explicit Nudity");
    }

    private IConsumer<string, string> BuildConsumer(string topic)
    {
        ConsumerConfig config = new()
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        return consumer;
    }
}
