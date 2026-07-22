using RentifyxAiServices.SharedLibrary.Events;

namespace RentifyxAiServices.Moderation;

public sealed record ModerationScanResult(
    IReadOnlyList<ModerationLabel> Labels,
    bool Succeeded,
    string? FailureReason);
