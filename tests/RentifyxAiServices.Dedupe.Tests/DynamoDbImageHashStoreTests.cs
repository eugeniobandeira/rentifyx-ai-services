using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Moq;
using Xunit;

namespace RentifyxAiServices.Dedupe.Tests;

public class DynamoDbImageHashStoreTests
{
    private readonly Mock<IAmazonDynamoDB> _dynamoDb = new();
    private static readonly Guid AssetId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task FindExistingAssetIdAsync_NoItem_ReturnsNull()
    {
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { IsItemSet = false });

        DynamoDbImageHashStore store = new(_dynamoDb.Object, "hash-table");

        Guid? result = await store.FindExistingAssetIdAsync("abc123");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindExistingAssetIdAsync_ItemExists_ReturnsStoredAssetId()
    {
        _dynamoDb
            .Setup(d => d.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse
            {
                IsItemSet = true,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["AssetId"] = new(AssetId.ToString())
                }
            });

        DynamoDbImageHashStore store = new(_dynamoDb.Object, "hash-table");

        Guid? result = await store.FindExistingAssetIdAsync("abc123");

        result.Should().Be(AssetId);
    }

    [Fact]
    public async Task RecordAsync_FirstSeen_WritesHashAssetIdAndTtl()
    {
        PutItemRequest? capturedRequest = null;
        _dynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutItemRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new PutItemResponse());

        DynamoDbImageHashStore store = new(_dynamoDb.Object, "hash-table");

        await store.RecordAsync("abc123", AssetId, TimeSpan.FromDays(365));

        capturedRequest.Should().NotBeNull();
        capturedRequest!.TableName.Should().Be("hash-table");
        capturedRequest.Item["ImageHash"].S.Should().Be("abc123");
        capturedRequest.Item["AssetId"].S.Should().Be(AssetId.ToString());
        capturedRequest.Item["ExpiresAt"].N.Should().NotBeNullOrEmpty();
        capturedRequest.ConditionExpression.Should().Contain("attribute_not_exists");
    }

    [Fact]
    public async Task RecordAsync_HashAlreadyRecordedByAnotherCall_DoesNotThrow()
    {
        _dynamoDb
            .Setup(d => d.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("already exists"));

        DynamoDbImageHashStore store = new(_dynamoDb.Object, "hash-table");

        Func<Task> act = async () => await store.RecordAsync("abc123", AssetId, TimeSpan.FromDays(365));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FindExistingAssetIdAsync_NullOrWhitespaceHash_Throws()
    {
        DynamoDbImageHashStore store = new(_dynamoDb.Object, "hash-table");

        Func<Task> act = async () => await store.FindExistingAssetIdAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
