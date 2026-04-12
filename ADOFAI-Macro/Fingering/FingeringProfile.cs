using ADOFAI_Macro.Models;
namespace ADOFAI_Macro.Fingering;

public sealed class FingeringProfile
{
    public required IReadOnlyList<FingerKey> UsableKeys { get; init; }

    public double PseudoChordThresholdMs { get; init; } = 30.0;
    public double SameKeyAvoidThresholdMs { get; init; } = 80.0;
    public required FingeringDensityProfile DensityProfile { get; init; }
}