using System;
using System.Collections.Generic;

namespace Macro_Inserter;

internal sealed class InputPlanEntry
{
    static InputPlanEntry()
    {
        // RuntimeInitializeOnLoadMethod is not always reliable for UMM-loaded assemblies.
        // This guarantees the compatibility patch is installed as soon as the scheduler
        // starts constructing input-plan entries.
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
        RawEntryCount = Math.Max(1, rawEntryCount);

        // Safety: gameplay must never emit fewer hits than floors unless a caller
        // explicitly marks the entry as compressed. The old BuildInputPlan creates
        // pseudoChord groups with isCompressed=false, so this prevents it from
        // silently skipping floors when the runtime patch was installed too late
        // for the current BuildInputPlan call.
        EmittedHitCount = isCompressed
            ? Math.Max(1, emittedHitCount)
            : Math.Max(1, RawEntryCount);

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

    // Compatibility fallback: if the original InternalMacroService creates a
    // grouped entry before our BuildInputPlan prefix is active, keep the grouped
    // firing path enabled so every floor in that group can still be hit.
    public bool IsPseudoChordGroup => RawEntryCount > 1;

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
