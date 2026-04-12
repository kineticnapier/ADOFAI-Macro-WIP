using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public sealed class FingeringNote
{
    public required ChartNote Note { get; init; }

    public double DeltaMs { get; init; }
    public bool IsPseudoChord { get; init; }
}