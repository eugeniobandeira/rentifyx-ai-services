using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Lambda.SNSEvents;
using Amazon.S3;
using Amazon.SQS;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;

// Required for the Lambda runtime to deserialize SNSEvent off the wire - see ModerationHandler's
// identical assembly attribute for the real bug this guards against (confirmed 2026-07-24).
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentifyxAiServices.Dedupe;

public sealed class DedupeHandler
{
    // The bucket fans S3 ObjectCreated out to both Moderation and Dedupe via
    // an SNS topic (iac/modules/s3-trigger) - see ModerationHandler's
    // identical comment for the real "ambiguously defined" S3 notification
    // error this guards against (confirmed 2026-07-27).
    private static readonly JsonSerializerOptions MessageJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DedupeService _service;

    public DedupeHandler() : this(BuildService())
    {
    }

    internal DedupeHandler(DedupeService service)
    {
        _service = service;
    }

    public async Task FunctionHandler(SNSEvent? snsEvent, ILambdaContext context)
    {
        if (snsEvent?.Records is null || snsEvent.Records.Count == 0)
        {
            context.Logger.LogWarning("Received empty or malformed SNS event, skipping");
            return;
        }

        foreach (SNSEvent.SNSRecord snsRecord in snsEvent.Records)
        {
            S3Event? s3Event;
            try
            {
                s3Event = JsonSerializer.Deserialize<S3Event>(snsRecord.Sns.Message, MessageJsonOptions);
            }
            catch (JsonException)
            {
                context.Logger.LogWarning("SNS message was not valid JSON, skipping");
                continue;
            }

            if (s3Event?.Records is null)
            {
                context.Logger.LogWarning("SNS message did not contain a valid S3 event, skipping");
                continue;
            }

            foreach (S3Event.S3EventNotificationRecord record in s3Event.Records)
            {
                await _service.ProcessAsync(record).ConfigureAwait(false);
            }
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
