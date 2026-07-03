using System;
using System.Collections.Generic;

namespace Macro_Inserter;

internal sealed class InputPlanEntry
{
    static InputPlanEntry()
    {
        // RuntimeInitializeOnLoadMethod can be late or skipped depending on UMM load order.
        // This is a fallback only; ModuleInitializer should install the BuildInputPlan patch first.
        PseudoChordInputPlanFix.Install("InputPlanEntry static constructor");
    }

    public InputPlanEntry(
        int planStartIndex,
        int planEndIndexExclusive,
        int firstSeqId,
        int lastSeqId,
        double firstTargetTimeSeconds,
        double lastTargetTimeSeconds,
        int rawEntryCount,
        int emittedHitCount,
        bool isExactDuplicateGroup,
        bool containsMidspin,
        bool isNearMidspin,
        bool isCompressed = false,
        IReadOnlyList<double>? hitTargetTimeSeconds = null,
        IReadOnlyList<int>? expectedAfterSeqIds = null,
        bool isChartFileChord = false)
    {
        PlanStartIndex = Math.Max(0, planStartIndex);
        PlanEndIndexExclusive = Math.Max(PlanStartIndex + 1, planEndIndexExclusive);
        FirstSeqId = firstSeqId;
        LastSeqId = lastSeqId;
        FirstTargetTimeSeconds = firstTargetTimeSeconds;
        LastTargetTimeSeconds = lastTargetTimeSeconds;
        RawEntryCount = Math.Max(1, rawEntryCount);
        EmittedHitCount = Math.Max(1, emittedHitCount);
        IsExactDuplicateGroup = isExactDuplicateGroup;
        ContainsMidspin = containsMidspin;
        IsNearMidspin = isNearMidspin;
        IsCompressed = isCompressed;
        HitTargetTimeSeconds = hitTargetTimeSeconds ?? Array.Empty<double>();
        ExpectedAfterSeqIds = expectedAfterSeqIds ?? Array.Empty<int>();
        IsChartFileChord = isChartFileChord;
    }

    public int PlanStartIndex { get; }

    public int PlanEndIndexExclusive { get; }

    public int FirstSeqId { get; }

    public int LastSeqId { get; }

    public double FirstTargetTimeSeconds { get; }

    public double LastTargetTimeSeconds { get; }

    public double SpanMs => (LastTargetTimeSeconds - FirstTargetTimeSeconds) * 1000.0;

    public int RawEntryCount { get; }

    public int EmittedHitCount { get; }

    public bool ContainsMidspin { get; }

    public bool IsNearMidspin { get; }

    public bool IsExactDuplicateGroup { get; }

    public bool IsCompressed { get; }

    public bool IsChartFileChord { get; }

    public IReadOnlyList<double> HitTargetTimeSeconds { get; }

    public IReadOnlyList<int> ExpectedAfterSeqIds { get; }

    public bool IsPseudoChordGroup => RawEntryCount > 1 || IsChartFileChord;

    public double GetHitTargetTimeSeconds(int hitIndex)
    {
        if (HitTargetTimeSeconds.Count == 0)
        {
            return FirstTargetTimeSeconds;
        }

        if (hitIndex < 0)
        {
            return HitTargetTimeSeconds[0];
        }

        if (hitIndex >= HitTargetTimeSeconds.Count)
        {
            return HitTargetTimeSeconds[HitTargetTimeSeconds.Count - 1];
        }

        return HitTargetTimeSeconds[hitIndex];
    }

    public int GetExpectedAfterSeqId(int hitIndex)
    {
        if (ExpectedAfterSeqIds.Count == 0)
        {
            return LastSeqId;
        }

        if (hitIndex < 0)
        {
            return ExpectedAfterSeqIds[0];
        }

        if (hitIndex >= ExpectedAfterSeqIds.Count)
        {
            return ExpectedAfterSeqIds[ExpectedAfterSeqIds.Count - 1];
        }

        return ExpectedAfterSeqIds[hitIndex];
    }
}
