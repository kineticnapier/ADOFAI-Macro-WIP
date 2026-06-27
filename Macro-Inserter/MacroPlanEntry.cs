namespace Macro_Inserter;

internal sealed class MacroPlanEntry
{
    public MacroPlanEntry(
        int seqId,
        double targetTimeSeconds,
        bool isMidspin = false,
        bool isNearMidspin = false,
        bool isNearSpeedChange = false,
        SpeedBand speedBand = SpeedBand.Normal)
    {
        SeqId = seqId;
        TargetTimeSeconds = targetTimeSeconds;
        IsMidspin = isMidspin;
        IsNearMidspin = isNearMidspin;
        IsNearSpeedChange = isNearSpeedChange;
        SpeedBand = speedBand;
    }

    public int SeqId { get; }

    public double TargetTimeSeconds { get; }

    public bool IsMidspin { get; }

    public bool IsNearMidspin { get; }

    public bool IsNearSpeedChange { get; }

    public SpeedBand SpeedBand { get; }
}
