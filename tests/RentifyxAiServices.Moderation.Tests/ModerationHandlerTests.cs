using System.Text.Json;
using Amazon.Lambda.S3Events;
using Amazon.Lambda.SNSEvents;
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
        SNSEvent snsEvent = new() { Records = [] };

        Func<Task> act = async () => await handler.FunctionHandler(snsEvent, context);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FunctionHandler_SnsMessageContainsValidS3Event_DispatchesToService()
    {
        Mock<IKeyConventionFilter> keyFilter = new();
        keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(false);
        ModerationService service = BuildService(keyFilter);
        ModerationHandler handler = new(service);
        TestLambdaContext context = new();
        SNSEvent snsEvent = WrapInSnsEvent(CreateS3Event());

        await handler.FunctionHandler(snsEvent, context);

        keyFilter.Verify(f => f.Matches(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_SnsMessageIsNotValidJson_DoesNotThrow()
    {
        ModerationHandler handler = new(BuildServiceWithNoDependencies());
        TestLambdaContext context = new();
        SNSEvent snsEvent = new()
        {
            Records = [new SNSEvent.SNSRecord { Sns = new SNSEvent.SNSMessage { Message = "not json" } }]
        };

        Func<Task> act = async () => await handler.FunctionHandler(snsEvent, context);

        await act.Should().NotThrowAsync();
    }

    private static S3Event CreateS3Event() => new()
    {
        Records =
        [
            new S3Event.S3EventNotificationRecord
            {
                S3 = new S3Event.S3Entity
                {
                    Bucket = new S3Event.S3BucketEntity { Name = "media-bucket" },
                    Object = new S3Event.S3ObjectEntity
                    {
                        Key = "assets/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/photo.jpg",
                        ETag = "etag-1"
                    }
                }
            }
        ]
    };

    private static SNSEvent WrapInSnsEvent(S3Event s3Event) => new()
    {
        Records =
        [
            new SNSEvent.SNSRecord { Sns = new SNSEvent.SNSMessage { Message = JsonSerializer.Serialize(s3Event) } }
        ]
    };

    private static ModerationService BuildServiceWithNoDependencies()
    {
        Mock<IKeyConventionFilter> keyFilter = new();
        keyFilter.Setup(f => f.Matches(It.IsAny<string>())).Returns(false);

        return BuildService(keyFilter);
    }

    private static ModerationService BuildService(Mock<IKeyConventionFilter> keyFilter) => new(
        keyFilter.Object,
        Mock.Of<IIdempotencyStore>(),
        Mock.Of<IRekognitionModerationClient>(),
        Mock.Of<IThresholdEvaluator>(),
        Mock.Of<IModerationEventPublisher>(),
        Mock.Of<Amazon.SQS.IAmazonSQS>(),
        "https://sqs.test/failure-dlq",
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ModerationService>.Instance);
}
