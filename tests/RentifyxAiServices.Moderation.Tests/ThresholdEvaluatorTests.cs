using FluentAssertions;
using Xunit;

namespace RentifyxAiServices.Moderation.Tests;

public class ThresholdEvaluatorTests
{
    private readonly ThresholdEvaluator _evaluator = new();

    [Theory]
    [InlineData(59f, Verdict.Approved)]
    [InlineData(60f, Verdict.PendingReview)]
    [InlineData(90f, Verdict.Rejected)]
    [InlineData(90.1f, Verdict.Rejected)]
    public void Evaluate_BoundaryConfidence_ReturnsExpectedVerdict(float confidence, Verdict expected)
    {
        List<ModerationLabel> labels = [new ModerationLabel("Explicit Nudity", confidence)];

        Verdict verdict = _evaluator.Evaluate(labels);

        verdict.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_NoLabels_ReturnsApproved()
    {
        Verdict verdict = _evaluator.Evaluate([]);

        verdict.Should().Be(Verdict.Approved);
    }
}
