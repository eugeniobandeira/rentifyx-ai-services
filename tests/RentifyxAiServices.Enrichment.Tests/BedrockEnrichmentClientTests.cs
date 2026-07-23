using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RentifyxAiServices.Enrichment.Bedrock;
using Xunit;

namespace RentifyxAiServices.Enrichment.Tests;

public class BedrockEnrichmentClientTests
{
    private readonly Mock<IAmazonBedrockRuntime> _bedrock = new();

    private static ConverseResponse SuccessResponse(string description, params string[] tags)
    {
        Document input = Document.FromObject(new Dictionary<string, object>
        {
            ["description"] = description,
            ["tags"] = tags.Cast<object>().ToList(),
        });

        return new ConverseResponse
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Role = ConversationRole.Assistant,
                    Content =
                    [
                        new ContentBlock
                        {
                            ToolUse = new ToolUseBlock { ToolUseId = "1", Name = "suggest_enrichment", Input = input },
                        },
                    ],
                },
            },
        };
    }

    [Fact]
    public async Task GenerateAsync_Success_MapsDescriptionAndTags()
    {
        _bedrock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessResponse("A cozy two-bedroom apartment", "balcony", "renovated"));

        BedrockEnrichmentClient client = new(_bedrock.Object, NullLogger<BedrockEnrichmentClient>.Instance);

        EnrichmentResult result = await client.GenerateAsync([1, 2, 3]);

        result.Succeeded.Should().BeTrue();
        result.Description.Should().Be("A cozy two-bedroom apartment");
        result.Tags.Should().BeEquivalentTo("balcony", "renovated");
    }

    [Fact]
    public async Task GenerateAsync_RequestAlwaysCapsMaxTokens()
    {
        ConverseRequest? captured = null;
        _bedrock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConverseRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(SuccessResponse("desc", "tag"));

        BedrockEnrichmentClient client = new(_bedrock.Object, NullLogger<BedrockEnrichmentClient>.Instance);

        await client.GenerateAsync([1, 2, 3]);

        captured.Should().NotBeNull();
        captured!.InferenceConfig.MaxTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateAsync_ThrottledThenSucceeds_RetriesAndReturnsSuccess()
    {
        int callCount = 0;
        _bedrock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 2)
                {
                    throw new ThrottlingException("throttled");
                }

                return SuccessResponse("desc", "tag");
            });

        BedrockEnrichmentClient client = new(_bedrock.Object, NullLogger<BedrockEnrichmentClient>.Instance);

        EnrichmentResult result = await client.GenerateAsync([1, 2, 3]);

        result.Succeeded.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GenerateAsync_ThrottledExhausted_ReturnsFailure()
    {
        _bedrock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ThrottlingException("throttled"));

        BedrockEnrichmentClient client = new(_bedrock.Object, NullLogger<BedrockEnrichmentClient>.Instance);

        EnrichmentResult result = await client.GenerateAsync([1, 2, 3]);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateAsync_SchemaMismatch_ReturnsFailure()
    {
        ConverseResponse response = new()
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Role = ConversationRole.Assistant,
                    Content = [new ContentBlock { Text = "I refuse to use a tool." }],
                },
            },
        };
        _bedrock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        BedrockEnrichmentClient client = new(_bedrock.Object, NullLogger<BedrockEnrichmentClient>.Instance);

        EnrichmentResult result = await client.GenerateAsync([1, 2, 3]);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateAsync_ToolInputMissingRequiredField_ReturnsFailure()
    {
        Document input = Document.FromObject(new Dictionary<string, object> { ["description"] = "desc only" });
        ConverseResponse response = new()
        {
            Output = new ConverseOutput
            {
                Message = new Message
                {
                    Role = ConversationRole.Assistant,
                    Content = [new ContentBlock { ToolUse = new ToolUseBlock { ToolUseId = "1", Name = "suggest_enrichment", Input = input } }],
                },
            },
        };
        _bedrock
            .Setup(b => b.ConverseAsync(It.IsAny<ConverseRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        BedrockEnrichmentClient client = new(_bedrock.Object, NullLogger<BedrockEnrichmentClient>.Instance);

        EnrichmentResult result = await client.GenerateAsync([1, 2, 3]);

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }
}
