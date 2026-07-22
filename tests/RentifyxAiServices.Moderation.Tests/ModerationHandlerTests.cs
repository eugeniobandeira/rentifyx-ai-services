using Amazon.Lambda.S3Events;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using Moq;
using Xunit;

namespace RentifyxAiServices.Moderation.Tests;

public class ModerationHandlerTests
{
    [Fact]
    public async Task FunctionHandler_MalformedEmptyEvent_DoesNotThrow()
    {
        ModerationHandler handler = new(BuildServiceWithNoDependencies());
        TestLambdaContext context = new();

        Func<Task> act = async () => await handler.FunctionHandler(null, context);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FunctionHandler_EventWithNoRecords_DoesNotThrow()
    {
        ModerationHandler handler = new(BuildServiceWithNoDependencies());
        TestLambdaContext context = new();
        S3Event s3Event = new() { Records = [] };

        Func<Task> act = async () => await handler.FunctionHandler(s3Event, context);

        await act.Should().NotThrowAsync();
    }

    private static ModerationService BuildServiceWithNoDependencies()
    {
        Mock<IKeyConventionFilter> keyFilter = new();
        keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(false);

        return new ModerationService(
            keyFilter.Object,
            Mock.Of<RentifyxAiServices.SharedLibrary.Idempotency.IIdempotencyStore>(),
            Mock.Of<IRekognitionModerationClient>(),
            Mock.Of<IThresholdEvaluator>(),
            Mock.Of<IModerationEventPublisher>(),
            Mock.Of<Amazon.SQS.IAmazonSQS>(),
            "https://sqs.test/failure-dlq",
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ModerationService>.Instance);
    }
}
