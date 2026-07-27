namespace RentifyxAiServices.Dedupe.Storage;

public interface IImageHashStore
{
    /// <summary>Returns the assetId already recorded under this hash, or null if the hash is new.</summary>
    Task<Guid?> FindExistingAssetIdAsync(string imageHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records this hash -> assetId mapping. Only ever called for a hash that
    /// <see cref="FindExistingAssetIdAsync"/> just reported as new - the first asset to use a given
    /// photo is what every later duplicate gets compared against, never overwritten.
    /// </summary>
    Task RecordAsync(string imageHash, Guid assetId, TimeSpan ttl, CancellationToken cancellationToken = default);
}
