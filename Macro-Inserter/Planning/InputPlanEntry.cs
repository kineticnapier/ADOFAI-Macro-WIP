using System;
using System.Collections.Generic;

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
        bool isNearMidspin,
        bool isExactDuplicateGroup = false,
        bool isCompressed = false,
        IReadOnlyList<double>? hitTargetTimeSeconds = null)
    {
        PlanStartIndex = planStartIndex;
        PlanEndIndexExclusive = planEndIndexExclusive;
        FirstSeqId = firstSeqId;
        LastSeqId = lastSeqId;
        FirstTargetTimeSeconds = firstTargetTimeSeconds;
        LastTargetTimeSeconds = lastTargetTimeSeconds;
        RawEntryCount = rawEntryCount;
        EmittedHitCount = Math.Max(1, emittedHitCount);
        ContainsMidspin = containsMidspin;
        IsNearMidspin = isNearMidspin;
        IsExactDuplicateGroup = isExactDuplicateGroup;
        IsCompressed = isCompressed;
        HitTargetTimeSeconds = hitTargetTimeSeconds ?? Array.Empty<double>();
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

    public IReadOnlyList<double> HitTargetTimeSeconds { get; }

    public bool IsPseudoChordGroup => RawEntryCount > 1 && IsCompressed;

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
}
