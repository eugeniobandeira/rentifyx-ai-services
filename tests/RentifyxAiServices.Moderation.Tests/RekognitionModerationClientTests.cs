using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace RentifyxAiServices.Moderation.Tests;

public class RekognitionModerationClientTests
{
    private readonly Mock<IAmazonRekognition> _rekognition = new();

    [Fact]
    public async Task ScanAsync_Success_MapsLabels()
    {
        DetectModerationLabelsResponse response = new()
        {
            ModerationLabels =
            [
                new Amazon.Rekognition.Model.ModerationLabel { Name = "Explicit Nudity", Confidence = 95.5f }
            ]
        };
        _rekognition
            .Setup(r => r.DetectModerationLabelsAsync(It.IsAny<DetectModerationLabelsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        RekognitionModerationClient client = new(_rekognition.Object, NullLogger<RekognitionModerationClient>.Instance);

        ModerationScanResult result = await client.ScanAsync("bucket", "assets/owner/asset/file.jpg");

        result.Succeeded.Should().BeTrue();
        result.Labels.Should().ContainSingle(l => l.Name == "Explicit Nudity");
        result.Labels[0].Confidence.Should().BeApproximately(95.5f, 0.001f);
    }

    [Fact]
    public async Task ScanAsync_ThrottledThenSucceeds_RetriesAndReturnsSuccess()
    {
        int callCount = 0;
        _rekognition
            .Setup(r => r.DetectModerationLabelsAsync(It.IsAny<DetectModerationLabelsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount < 2)
                {
                    throw new ThrottlingException("throttled");
                }

                return new DetectModerationLabelsResponse { ModerationLabels = [] };
            });

        RekognitionModerationClient client = new(_rekognition.Object, NullLogger<RekognitionModerationClient>.Instance);

        ModerationScanResult result = await client.ScanAsync("bucket", "assets/owner/asset/file.jpg");

        result.Succeeded.Should().BeTrue();
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ScanAsync_ThrottledExhausted_ReturnsFailure()
    {
        _rekognition
            .Setup(r => r.DetectModerationLabelsAsync(It.IsAny<DetectModerationLabelsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ThrottlingException("throttled"));

        RekognitionModerationClient client = new(_rekognition.Object, NullLogger<RekognitionModerationClient>.Instance);

        ModerationScanResult result = await client.ScanAsync("bucket", "assets/owner/asset/file.jpg");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ScanAsync_MalformedImage_ReturnsFailureWithoutRetry()
    {
        int callCount = 0;
        _rekognition
            .Setup(r => r.DetectModerationLabelsAsync(It.IsAny<DetectModerationLabelsRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(() => Task.FromException<DetectModerationLabelsResponse>(new InvalidImageFormatException("bad image")));

        RekognitionModerationClient client = new(_rekognition.Object, NullLogger<RekognitionModerationClient>.Instance);

        ModerationScanResult result = await client.ScanAsync("bucket", "assets/owner/asset/file.jpg");

        result.Succeeded.Should().BeFalse();
        callCount.Should().Be(1);
    }
}
