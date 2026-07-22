namespace RentifyxAiServices.Shared.Idempotency;

public interface IIdempotencyStore
{
    /// <summary>Claims <paramref name="idempotencyKey"/> for the first time. Returns false if it was already claimed.</summary>
    Task<bool> TryMarkProcessedAsync(string idempotencyKey, TimeSpan ttl, CancellationToken cancellationToken = default);
}
