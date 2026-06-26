namespace Macro_Inserter;

internal sealed class MacroPlanEntry
{
    public MacroPlanEntry(int seqId, double targetTimeSeconds)
    {
        SeqId = seqId;
        TargetTimeSeconds = targetTimeSeconds;
    }

    public int SeqId { get; }

    public double TargetTimeSeconds { get; }
}
