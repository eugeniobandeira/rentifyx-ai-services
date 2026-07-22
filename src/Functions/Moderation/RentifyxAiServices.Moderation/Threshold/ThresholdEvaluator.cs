namespace RentifyxAiServices.Moderation.Threshold;

/// <summary>Boundaries per ADR-AI-003: &gt;=90% Rejected, [60,90) PendingReview, &lt;60% Approved.</summary>
public sealed class ThresholdEvaluator : IThresholdEvaluator
{
    private const float RejectThreshold = 90f;
    private const float ReviewThreshold = 60f;

    public Verdict Evaluate(IReadOnlyList<ModerationLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        if (labels.Count == 0)
        {
            return Verdict.Approved;
        }

        float topConfidence = labels.Max(l => l.Confidence);

        if (topConfidence >= RejectThreshold)
        {
            return Verdict.Rejected;
        }

        if (topConfidence >= ReviewThreshold)
        {
            return Verdict.PendingReview;
        }

        return Verdict.Approved;
    }
}
