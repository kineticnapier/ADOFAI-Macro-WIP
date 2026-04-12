namespace ADOFAI_Macro;

public sealed class MacroSettings
{
    public double GlobalOffsetMs { get; init; } = 0.0;
    public int PseudoChordThreshold { get; init; } = 30;
    public int StreamAngle { get; init; } = 45;

    public double NormalHoldMs { get; init; } = 40.0;
    public double StreamHoldMs { get; init; } = 3.0;
    public double ReleaseLeadMs { get; init; } = 10.0;
}