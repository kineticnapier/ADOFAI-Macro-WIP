namespace Macro_Inserter;

internal sealed class InputPlanEntry
{
    public InputPlanEntry(
        int planStartIndex,
        int planEndIndexExclusive,
        int firstSeqId,
        int lastSeqId,
        double firstTargetTimeSeconds,
        double lastTargetTimeSeconds,
        int rawEntryCount,
        int emittedHitCount,
        bool containsMidspin,
        bool isNearMidspin)
    {
        PlanStartIndex = planStartIndex;
        PlanEndIndexExclusive = planEndIndexExclusive;
        FirstSeqId = firstSeqId;
        LastSeqId = lastSeqId;
        FirstTargetTimeSeconds = firstTargetTimeSeconds;
        LastTargetTimeSeconds = lastTargetTimeSeconds;
        RawEntryCount = rawEntryCount;
        EmittedHitCount = emittedHitCount;
        ContainsMidspin = containsMidspin;
        IsNearMidspin = isNearMidspin;
    }

    public int PlanStartIndex { get; }

    public int PlanEndIndexExclusive { get; }

    public int FirstSeqId { get; }

    public int LastSeqId { get; }

    public double FirstTargetTimeSeconds { get; }

    public double LastTargetTimeSeconds { get; }

    public int RawEntryCount { get; }

    public int EmittedHitCount { get; }

    public bool ContainsMidspin { get; }

    public bool IsNearMidspin { get; }

    public bool IsPseudoChordGroup => RawEntryCount > 1;
}
