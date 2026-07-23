namespace RentifyxAiServices.SharedKernel.Kafka;

public interface IEventPublisher<in TEvent>
{
    Task PublishAsync(string key, TEvent payload, CancellationToken cancellationToken = default);
}
