using Amazon.Lambda.S3Events;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using Moq;
using Xunit;

namespace RentifyxAiServices.Dedupe.Tests;

public class DedupeHandlerTests
{
    [Fact]
    public async Task FunctionHandler_MalformedEmptyEvent_DoesNotThrow()
    {
        DedupeHandler handler = new(BuildServiceWithNoDependencies());
        TestLambdaContext context = new();

        Func<Task> act = async () => await handler.FunctionHandler(null, context);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FunctionHandler_EventWithNoRecords_DoesNotThrow()
    {
        DedupeHandler handler = new(BuildServiceWithNoDependencies());
        TestLambdaContext context = new();
        S3Event s3Event = new() { Records = [] };

        Func<Task> act = async () => await handler.FunctionHandler(s3Event, context);

        await act.Should().NotThrowAsync();
    }

    private static DedupeService BuildServiceWithNoDependencies()
    {
        Mock<IKeyConventionFilter> keyFilter = new();
        keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(false);

        return new DedupeService(
            keyFilter.Object,
            Mock.Of<IIdempotencyStore>(),
            Mock.Of<Amazon.S3.IAmazonS3>(),
            Mock.Of<IPerceptualHashCalculator>(),
            Mock.Of<IImageHashStore>(),
            Mock.Of<IDedupeEventPublisher>(),
            Mock.Of<Amazon.SQS.IAmazonSQS>(),
            "https://sqs.test/failure-dlq",
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DedupeService>.Instance);
    }
}
