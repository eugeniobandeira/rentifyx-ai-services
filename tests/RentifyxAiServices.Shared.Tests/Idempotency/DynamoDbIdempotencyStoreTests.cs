using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Moq;
using RentifyxAiServices.SharedLibrary.Idempotency;
using Xunit;

namespace RentifyxAiServices.Shared.Tests.Idempotency;

public class DynamoDbIdempotencyStoreTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();

    [Fact]
    public async Task TryMarkProcessedAsync_FirstSeen_ReturnsTrueAndWritesTtl()
    {
        PutItemRequest? capturedRequest = null;
        _dynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutItemRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new PutItemResponse());

        DynamoDbIdempotencyStore store = new(_dynamoDb.Object, "idempotency-table");

        bool claimed = await store.TryMarkProcessedAsync("bucket/key#etag", TimeSpan.FromHours(1));

        claimed.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.TableName.Should().Be("idempotency-table");
        capturedRequest.Item["IdempotencyKey"].S.Should().Be("bucket/key#etag");
        capturedRequest.Item["ExpiresAt"].N.Should().NotBeNullOrEmpty();
        capturedRequest.ConditionExpression.Should().Contain("attribute_not_exists");
    }

    [Fact]
    public async Task TryMarkProcessedAsync_DuplicateKey_ReturnsFalse()
    {
        _dynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("already exists"));

        DynamoDbIdempotencyStore store = new(_dynamoDb.Object, "idempotency-table");

        bool claimed = await store.TryMarkProcessedAsync("bucket/key#etag", TimeSpan.FromHours(1));

        claimed.Should().BeFalse();
    }

    [Fact]
    public async Task TryMarkProcessedAsync_NullOrWhitespaceKey_Throws()
    {
        DynamoDbIdempotencyStore store = new(_dynamoDb.Object, "idempotency-table");

        Func<Task> act = async () => await store.TryMarkProcessedAsync(" ", TimeSpan.FromHours(1));

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
