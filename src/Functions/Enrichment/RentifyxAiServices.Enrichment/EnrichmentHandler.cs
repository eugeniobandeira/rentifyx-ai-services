using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.KafkaEvents;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Amazon.SQS;
using Confluent.Kafka;
using Microsoft.Extensions.Logging.Abstractions;
using RentifyxAiServices.SharedKernel.Events;

// Required for the Lambda runtime to deserialize KafkaEvent off the wire -
// same LambdaValidationException bug found and fixed in ModerationHandler
// against a real S3 upload, 2026-07-24.
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace RentifyxAiServices.Enrichment;

public sealed class EnrichmentHandler
{
    private readonly EnrichmentService _service;

    public EnrichmentHandler() : this(BuildService())
    {
    }

    internal EnrichmentHandler(EnrichmentService service)
    {
        _service = service;
    }

    public async Task FunctionHandler(KafkaEvent? kafkaEvent, ILambdaContext context)
    {
        if (kafkaEvent?.Records is null || kafkaEvent.Records.Count == 0)
        {
            context.Logger.LogWarning("Received empty or malformed Kafka event, skipping");
            return;
        }

        foreach (IList<KafkaEvent.KafkaEventRecord> partitionRecords in kafkaEvent.Records.Values)
        {
            foreach (KafkaEvent.KafkaEventRecord record in partitionRecords)
            {
                AssetMediaModerated? moderatedEvent = DeserializeRecord(record, context);
                if (moderatedEvent is null)
                {
                    continue;
                }

                await _service.ProcessAsync(moderatedEvent).ConfigureAwait(false);
            }
        }
    }

    private static AssetMediaModerated? DeserializeRecord(KafkaEvent.KafkaEventRecord record, ILambdaContext context)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AssetMediaModerated>(record.Value.ToArray());
        }
        catch (System.Text.Json.JsonException ex)
        {
            context.Logger.LogError($"Failed to deserialize Kafka record on topic {record.Topic}: {ex.Message}");
            return null;
        }
    }

    private static EnrichmentService BuildService()
    {
        string idempotencyTable = Environment.GetEnvironmentVariable("ENRICHMENT_IDEMPOTENCY_TABLE_NAME") ?? "enrichment-idempotency";
        string failureDlqUrl = Environment.GetEnvironmentVariable("ENRICHMENT_FAILURE_DLQ_URL") ?? string.Empty;
        string bedrockRegion = Environment.GetEnvironmentVariable("BEDROCK_REGION") ?? "us-east-1";

        IAmazonDynamoDB dynamoDb = new AmazonDynamoDBClient();
        IAmazonS3 s3 = new AmazonS3Client();
        IAmazonSQS sqs = new AmazonSQSClient();
        Amazon.BedrockRuntime.IAmazonBedrockRuntime bedrock = new Amazon.BedrockRuntime.AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.GetBySystemName(bedrockRegion));

        return new EnrichmentService(
            new RentifyxAiServices.SharedKernel.Idempotency.DynamoDbIdempotencyStore(dynamoDb, idempotencyTable),
            s3,
            new RentifyxAiServices.Enrichment.Bedrock.BedrockEnrichmentClient(bedrock, NullLogger<RentifyxAiServices.Enrichment.Bedrock.BedrockEnrichmentClient>.Instance),
            new RentifyxAiServices.Enrichment.Publishing.KafkaEnrichmentEventPublisher(
                new RentifyxAiServices.SharedKernel.Kafka.KafkaEventPublisher<RentifyxAiServices.SharedKernel.Events.AssetEnrichmentSuggested>(
                    BuildProducer(),
                    Environment.GetEnvironmentVariable("KAFKA_ENRICHMENT_SUGGESTED_TOPIC") ?? "asset-enrichment-suggested")),
            sqs,
            failureDlqUrl,
            NullLogger<EnrichmentService>.Instance);
    }

    private static IProducer<string, string> BuildProducer()
    {
        string kafkaBootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? string.Empty;
        ProducerConfig producerConfig = new() { BootstrapServers = kafkaBootstrapServers };
        return new ProducerBuilder<string, string>(producerConfig).Build();
    }
}
