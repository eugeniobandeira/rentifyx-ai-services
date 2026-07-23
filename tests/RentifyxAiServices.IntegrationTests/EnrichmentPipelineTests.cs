using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentifyxAiServices.Enrichment;
using Testcontainers.Kafka;
using Testcontainers.LocalStack;
using Xunit;

namespace RentifyxAiServices.IntegrationTests;

/// <summary>
/// E-03 ENR-01/02/03/04: AssetMediaModerated(Approved) -> Bedrock (stubbed, LocalStack community
/// doesn't support it) -> AssetEnrichmentSuggested on Kafka, against real LocalStack S3/DynamoDB
/// and a real Kafka broker via Testcontainers. No AWS credentials used.
/// </summary>
public sealed class EnrichmentPipelineTests : IAsyncLifetime
{
    private const string Bucket = "media-bucket";
    private const string Table = "enrichment-idempotency";
    private const string SuggestedTopic = "asset-enrichment-suggested";
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

        Amazon.Runtime.BasicAWSCredentials credentials = new("test", "test");

        _s3 = new AmazonS3Client(credentials, new AmazonS3Config { ServiceURL = _localStack.GetConnectionString(), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
        _dynamoDb = new AmazonDynamoDBClient(credentials, new AmazonDynamoDBConfig { ServiceURL = _localStack.GetConnectionString(), AuthenticationRegion = "us-east-1" });

        await _s3.PutBucketAsync(Bucket);
        await _dynamoDb.CreateTableAsync(new CreateTableRequest
        {
            TableName = Table,
            AttributeDefinitions = [new AttributeDefinition("IdempotencyKey", ScalarAttributeType.S)],
            KeySchema = [new KeySchemaElement("IdempotencyKey", KeyType.HASH)],
            BillingMode = BillingMode.PAY_PER_REQUEST,
        });

        _producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build();

        using IAdminClient adminClient = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _kafka.GetBootstrapAddress() }).Build();
        await adminClient.CreateTopicsAsync(
        [
            new TopicSpecification { Name = SuggestedTopic, NumPartitions = 1, ReplicationFactor = 1 },
        ]).ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        _producer.Dispose();
        await Task.WhenAll(_localStack.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    private EnrichmentService CreateService(IBedrockEnrichmentClient bedrockClient) =>
        new(
            new DynamoDbIdempotencyStore(_dynamoDb, Table),
            _s3,
            bedrockClient,
            new KafkaEnrichmentEventPublisher(new KafkaEventPublisher<AssetEnrichmentSuggested>(_producer, SuggestedTopic)),
            Mock.Of<IAmazonSQS>(),
            "unused-failure-dlq",
            NullLogger<EnrichmentService>.Instance);

    private static AssetMediaModerated CreateEvent(Verdict verdict = Verdict.Approved) =>
        new(AssetId, verdict, [], 10f, DateTimeOffset.UtcNow, Bucket, Key);

    private static Mock<IBedrockEnrichmentClient> StubBedrock(bool succeeded = true) =>
        new Func<Mock<IBedrockEnrichmentClient>>(() =>
        {
            Mock<IBedrockEnrichmentClient> mock = new();
            mock.Setup(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(succeeded
                    ? new EnrichmentResult("A cozy two-bedroom apartment", ["balcony", "renovated"], true, null)
                    : new EnrichmentResult(null, [], false, "stubbed failure"));
            return mock;
        })();

    [Fact]
    public async Task ProcessAsync_ApprovedAsset_PublishesEnrichmentSuggestedToKafka()
    {
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = Key, ContentBody = "fake-image-bytes" });
        Mock<IBedrockEnrichmentClient> bedrock = StubBedrock();
        EnrichmentService service = CreateService(bedrock.Object);

        using IConsumer<string, string> consumer = BuildConsumer(SuggestedTopic);

        await service.ProcessAsync(CreateEvent());

        ConsumeResult<string, string> result = consumer.Consume(TimeSpan.FromSeconds(15));
        result.Should().NotBeNull();
        result.Message.Value.Should().Contain(AssetId.ToString()).And.Contain("balcony");
    }

    [Fact]
    public async Task ProcessAsync_SameAssetTwice_DoesNotInvokeBedrockTwice()
    {
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = Key, ContentBody = "fake-image-bytes" });
        Mock<IBedrockEnrichmentClient> bedrock = StubBedrock();
        EnrichmentService service = CreateService(bedrock.Object);

        AssetMediaModerated moderatedEvent = CreateEvent();
        await service.ProcessAsync(moderatedEvent);
        await service.ProcessAsync(moderatedEvent);

        bedrock.Verify(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(Verdict.Rejected)]
    [InlineData(Verdict.PendingReview)]
    public async Task ProcessAsync_NotApproved_PublishesNothing(Verdict verdict)
    {
        Mock<IBedrockEnrichmentClient> bedrock = StubBedrock();
        EnrichmentService service = CreateService(bedrock.Object);

        await service.ProcessAsync(CreateEvent(verdict));

        bedrock.Verify(b => b.GenerateAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private IConsumer<string, string> BuildConsumer(string topic)
    {
        ConsumerConfig config = new()
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };
        IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        return consumer;
    }
}
