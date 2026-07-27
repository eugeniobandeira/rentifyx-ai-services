using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.SQS;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;

// Required for the Lambda runtime to deserialize S3Event off the wire - see ModerationHandler's
// identical assembly attribute for the real bug this guards against (confirmed 2026-07-24).
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentifyxAiServices.Dedupe;

public sealed class DedupeHandler
{
    private readonly DedupeService _service;

    public DedupeHandler() : this(BuildService())
    {
    }

    internal DedupeHandler(DedupeService service)
    {
        _service = service;
    }

    public async Task FunctionHandler(S3Event? s3Event, ILambdaContext context)
    {
        if (s3Event?.Records is null || s3Event.Records.Count == 0)
        {
            context.Logger.LogWarning("Received empty or malformed S3 event, skipping");
            return;
        }

        foreach (S3Event.S3EventNotificationRecord record in s3Event.Records)
        {
            await _service.ProcessAsync(record).ConfigureAwait(false);
        }
    }

    private static DedupeService BuildService()
    {
        string idempotencyTable = Environment.GetEnvironmentVariable("IDEMPOTENCY_TABLE_NAME") ?? "dedupe-idempotency";
        string hashTable = Environment.GetEnvironmentVariable("HASH_TABLE_NAME") ?? "dedupe-image-hashes";
        string kafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? string.Empty;
        string duplicateTopic = Environment.GetEnvironmentVariable("KAFKA_DUPLICATE_DETECTED_TOPIC") ?? "asset-duplicate-detected";
        string failureDlqUrl = Environment.GetEnvironmentVariable("FAILURE_DLQ_URL") ?? string.Empty;

        IAmazonDynamoDB dynamoDb = new AmazonDynamoDBClient();
        IAmazonS3 s3 = new AmazonS3Client();
        IAmazonSQS sqs = new AmazonSQSClient();

        ProducerConfig producerConfig = new() { BootstrapServers = kafkaBootstrapServers };
        IProducer<string, string> producer = new ProducerBuilder<string, string>(producerConfig).Build();

        return new DedupeService(
            new AssetKeyConventionFilter(),
            new DynamoDbIdempotencyStore(dynamoDb, idempotencyTable),
            s3,
            new AverageHashCalculator(),
            new DynamoDbImageHashStore(dynamoDb, hashTable),
            new KafkaDedupeEventPublisher(new KafkaEventPublisher<AssetDuplicateDetected>(producer, duplicateTopic)),
            sqs,
            failureDlqUrl,
            NullLogger<DedupeService>.Instance);
    }
}
