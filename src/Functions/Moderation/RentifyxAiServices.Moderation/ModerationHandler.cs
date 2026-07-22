using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Rekognition;
using Amazon.SQS;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;

namespace RentifyxAiServices.Moderation;

public sealed class ModerationHandler
{
    private readonly ModerationService _service;

    public ModerationHandler() : this(BuildService())
    {
    }

    internal ModerationHandler(ModerationService service)
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

    private static ModerationService BuildService()
    {
        string idempotencyTable = Environment.GetEnvironmentVariable("IDEMPOTENCY_TABLE_NAME") ?? "moderation-idempotency";
        string kafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? string.Empty;
        string moderatedTopic = Environment.GetEnvironmentVariable("KAFKA_MODERATED_TOPIC") ?? "asset-media-moderated";
        string pendingReviewTopic = Environment.GetEnvironmentVariable("KAFKA_PENDING_REVIEW_TOPIC") ?? "asset-pending-manual-review";
        string reviewQueueUrl = Environment.GetEnvironmentVariable("REVIEW_QUEUE_URL") ?? string.Empty;
        string failureDlqUrl = Environment.GetEnvironmentVariable("FAILURE_DLQ_URL") ?? string.Empty;

        IAmazonDynamoDB dynamoDb = new AmazonDynamoDBClient();
        IAmazonRekognition rekognition = new AmazonRekognitionClient();
        IAmazonSQS sqs = new AmazonSQSClient();

        ProducerConfig producerConfig = new() { BootstrapServers = kafkaBootstrapServers };
        IProducer<string, string> producer = new ProducerBuilder<string, string>(producerConfig).Build();

        return new ModerationService(
            new AssetKeyConventionFilter(),
            new DynamoDbIdempotencyStore(dynamoDb, idempotencyTable),
            new RekognitionModerationClient(rekognition, NullLogger<RekognitionModerationClient>.Instance),
            new ThresholdEvaluator(),
            new KafkaModerationEventPublisher(
                new KafkaEventPublisher<AssetMediaModerated>(producer, moderatedTopic),
                new KafkaEventPublisher<AssetPendingManualReview>(producer, pendingReviewTopic),
                sqs,
                reviewQueueUrl),
            sqs,
            failureDlqUrl,
            NullLogger<ModerationService>.Instance);
    }
}
