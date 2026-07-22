using System.Text.Json;
using Confluent.Kafka;

namespace RentifyxAiServices.SharedLibrary.Kafka;

public sealed class KafkaEventPublisher<TEvent>(IProducer<string, string> producer, string topic) : IEventPublisher<TEvent>
{
    public async Task PublishAsync(string key, TEvent payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Message<string, string> message = new()
        {
            Key = key,
            Value = JsonSerializer.Serialize(payload)
        };

        await producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
    }
}
