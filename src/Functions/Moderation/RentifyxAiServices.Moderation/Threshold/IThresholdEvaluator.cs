using RentifyxAiServices.SharedLibrary.Events;

namespace RentifyxAiServices.Moderation;

public interface IThresholdEvaluator
{
    Verdict Evaluate(IReadOnlyList<ModerationLabel> labels);
}
