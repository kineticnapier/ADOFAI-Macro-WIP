namespace Macro_Inserter;

internal sealed class MacroPlanEntry
{
    public MacroPlanEntry(
        int seqId,
        double targetTimeSeconds,
        bool isMidspin = false,
        bool isNearMidspin = false,
        double? angleDegrees = null)
    {
        SeqId = seqId;
        TargetTimeSeconds = targetTimeSeconds;
        IsMidspin = isMidspin;
        IsNearMidspin = isNearMidspin;
        AngleDegrees = angleDegrees;
    }

    public int SeqId { get; }

    public double TargetTimeSeconds { get; }

    public bool IsMidspin { get; }

    public bool IsNearMidspin { get; }

    public double? AngleDegrees { get; }
}
