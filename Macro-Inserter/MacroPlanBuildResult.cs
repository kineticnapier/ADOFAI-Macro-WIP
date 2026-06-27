using System.Collections.Generic;

namespace Macro_Inserter;

internal sealed class MacroPlanBuildResult
{
    public MacroPlanBuildResult(
        IReadOnlyList<MacroPlanEntry> plan,
        string? failureReason,
        int skippedMidspinCount = 0,
        int skippedDuplicateTimeCount = 0)
    {
        Plan = plan;
        FailureReason = failureReason;
        SkippedMidspinCount = skippedMidspinCount;
        SkippedDuplicateTimeCount = skippedDuplicateTimeCount;
    }

    public IReadOnlyList<MacroPlanEntry> Plan { get; }

    public string? FailureReason { get; }

    public int SkippedMidspinCount { get; }

    public int SkippedDuplicateTimeCount { get; }
}
