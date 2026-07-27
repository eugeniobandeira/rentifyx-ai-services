using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace RentifyxAiServices.Dedupe.Storage;

public sealed class DynamoDbImageHashStore(IAmazonDynamoDB dynamoDb, string tableName) : IImageHashStore
{
    private const string HashAttribute = "ImageHash";
    private const string AssetIdAttribute = "AssetId";
    private const string TtlAttribute = "ExpiresAt";

    public async Task<Guid?> FindExistingAssetIdAsync(string imageHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageHash);

        GetItemRequest request = new()
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [HashAttribute] = new(imageHash)
            },
            ConsistentRead = true
        };

        GetItemResponse response = await dynamoDb.GetItemAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsItemSet || !response.Item.TryGetValue(AssetIdAttribute, out AttributeValue? assetIdValue))
        {
            return null;
        }

        return Guid.Parse(assetIdValue.S);
    }

    public async Task RecordAsync(string imageHash, Guid assetId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageHash);

        long expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

        PutItemRequest request = new()
        {
            TableName = tableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [HashAttribute] = new(imageHash),
                [AssetIdAttribute] = new(assetId.ToString()),
                [TtlAttribute] = new AttributeValue { N = expiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            },
            // First-seen wins: if two invocations race for the same brand-new hash, only one
            // recording survives - the other one's FindExistingAssetIdAsync call already ran
            // before this Put, so at worst both fail to flag each other as duplicates once (an
            // acceptable race for a Phase 2 exact-match check, not a correctness bug for the
            // stored mapping itself).
            ConditionExpression = $"attribute_not_exists({HashAttribute})"
        };

        try
        {
            await dynamoDb.PutItemAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ConditionalCheckFailedException)
        {
            // Someone else recorded this hash first - leave their mapping as the source of truth.
        }
    }
}
