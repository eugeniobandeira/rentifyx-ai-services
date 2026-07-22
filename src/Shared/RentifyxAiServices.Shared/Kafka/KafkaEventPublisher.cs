using Confluent.Kafka;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RentifyxAiServices.Shared.Kafka;

public sealed class KafkaEventPublisher<TEvent>(IProducer<string, string> producer, string topic) : IEventPublisher<TEvent>
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task PublishAsync(string key, TEvent payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Message<string, string> message = new()
        {
            Key = key,
            Value = JsonSerializer.Serialize(payload, SerializerOptions)
        };

        await producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
    }
}
