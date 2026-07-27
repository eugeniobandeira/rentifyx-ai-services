using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.Lambda.SNSEvents;
using Amazon.Rekognition;
using Amazon.SQS;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;

// Required for the Lambda runtime to deserialize SNSEvent off the wire -
// missing this fails every real invocation with LambdaValidationException
// (confirmed the hard way against a real S3 upload, 2026-07-24; unit/
// integration tests call ModerationHandler.FunctionHandler directly and
// never exercise the runtime's own deserialization path).
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentifyxAiServices.Moderation;

public sealed class ModerationHandler
{
    // The bucket fans S3 ObjectCreated out to both Moderation and Dedupe via
    // an SNS topic (iac/modules/s3-trigger) - a direct S3->Lambda
    // notification only supports one destination per overlapping
    // prefix/suffix filter, confirmed the hard way against real AWS
    // 2026-07-27 ("Configuration is ambiguously defined"). SNS relays the
    // exact same raw S3 event-notification JSON as a string in each
    // record's Sns.Message - deserializing that string into the same
    // S3Event shape keeps ProcessAsync/the rest of the pipeline unchanged.
    private static readonly JsonSerializerOptions MessageJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ModerationService _service;

    public ModerationHandler() : this(BuildService())
    {
    }

    internal ModerationHandler(ModerationService service)
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
