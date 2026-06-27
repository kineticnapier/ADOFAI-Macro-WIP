namespace Macro_Inserter;

internal sealed class MacroPlanEntry
{
    public MacroPlanEntry(int seqId, double targetTimeSeconds, bool isNearMidspin = false)
    {
        SeqId = seqId;
        TargetTimeSeconds = targetTimeSeconds;
        IsNearMidspin = isNearMidspin;
    }

    public int SeqId { get; }

    public double TargetTimeSeconds { get; }

    public bool IsNearMidspin { get; }
}
