namespace RentifyxAiServices.Moderation.Threshold;

public interface IThresholdEvaluator
{
    Verdict Evaluate(IReadOnlyList<ModerationLabel> labels);
}
