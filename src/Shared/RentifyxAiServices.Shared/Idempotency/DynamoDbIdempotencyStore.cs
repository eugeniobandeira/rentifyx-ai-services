using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace RentifyxAiServices.SharedLibrary.Idempotency;

public sealed class DynamoDbIdempotencyStore(IAmazonDynamoDB dynamoDb, string tableName) : IIdempotencyStore
{
    private const string KeyAttribute = "IdempotencyKey";
    private const string TtlAttribute = "ExpiresAt";

    public async Task<bool> TryMarkProcessedAsync(string idempotencyKey, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        long expiresAt = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

        PutItemRequest request = new()
        {
            TableName = tableName,
            Item = new Dictionary<string, AttributeValue>
            {
                [KeyAttribute] = new(idempotencyKey),
                [TtlAttribute] = new AttributeValue { N = expiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            },
            ConditionExpression = $"attribute_not_exists({KeyAttribute})"
        };

        try
        {
            await dynamoDb.PutItemAsync(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }
}
