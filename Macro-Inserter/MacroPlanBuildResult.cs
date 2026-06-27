using System.Collections.Generic;

namespace Macro_Inserter;

internal sealed class MacroPlanBuildResult
{
    public MacroPlanBuildResult(IReadOnlyList<MacroPlanEntry> plan, string? failureReason)
    {
        Plan = plan;
        FailureReason = failureReason;
    }

    public IReadOnlyList<MacroPlanEntry> Plan { get; }

    public string? FailureReason { get; }
}
