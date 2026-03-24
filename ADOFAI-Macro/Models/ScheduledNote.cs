namespace ADOFAI_Macro.Models;

public sealed record ScheduledNote(
    int Index,
    double TimeMs,
    FingerKey Key,
    long TargetTick,
    double RelativeAngle
);