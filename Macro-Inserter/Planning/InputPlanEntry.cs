using System;
using System.Collections.Generic;

namespace Macro_Inserter;

internal sealed class InputPlanEntry
{
    static InputPlanEntry()
    {
        // RuntimeInitializeOnLoadMethod can be timing-dependent under UMM.
        // Keep this as a backup; the module initializer in PseudoChordInputPlanFix
        // should normally install the patch before the scheduler is armed.
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
        IReadOnlyList<int>? hitExpectedAfterSeqIds = null,
        IReadOnlyList<double>? hitAnglesDegrees = null)
    {
        PlanStartIndex = planStartIndex;
        PlanEndIndexExclusive = planEndIndexExclusive;
        FirstSeqId = firstSeqId;
        LastSeqId = lastSeqId;
        FirstTargetTimeSeconds = firstTargetTimeSeconds;
        LastTargetTimeSeconds = lastTargetTimeSeconds;
        RawEntryCount = Math.Max(1, rawEntryCount);
        IsExactDuplicateGroup = isExactDuplicateGroup;
        ContainsMidspin = containsMidspin;
        IsNearMidspin = isNearMidspin;

        HitTargetTimeSeconds = hitTargetTimeSeconds ?? Array.Empty<double>();
        HitExpectedAfterSeqIds = hitExpectedAfterSeqIds ?? Array.Empty<int>();
        HitAnglesDegrees = hitAnglesDegrees ?? Array.Empty<double>();

        bool hasExplicitHitPlan = HitTargetTimeSeconds.Count > 0 || HitExpectedAfterSeqIds.Count > 0;
        IsCompressed = isCompressed || hasExplicitHitPlan || (isExactDuplicateGroup && emittedHitCount < RawEntryCount);

        if (IsCompressed)
        {
            int plannedHitCount = Math.Max(HitTargetTimeSeconds.Count, HitExpectedAfterSeqIds.Count);
            EmittedHitCount = Math.Max(1, plannedHitCount > 0 ? plannedHitCount : emittedHitCount);
        }
        else
        {
            // Non-compressed groups are not allowed to silently drop floors.
            EmittedHitCount = Math.Max(1, Math.Max(emittedHitCount, RawEntryCount));
        }
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

    public IReadOnlyList<int> HitExpectedAfterSeqIds { get; }

    public IReadOnlyList<double> HitAnglesDegrees { get; }

    public bool HasExplicitHitPlan => HitTargetTimeSeconds.Count > 0 || HitExpectedAfterSeqIds.Count > 0;

    // Keep grouped firing enabled for explicit compressed plans and also for old
    // grouped entries if a stale BuildInputPlan slips through before the patch.
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

    public int GetHitExpectedAfterSeqId(int hitIndex)
    {
        if (HitExpectedAfterSeqIds.Count == 0)
        {
            return Math.Min(FirstSeqId + Math.Max(0, hitIndex), LastSeqId);
        }

        if (hitIndex < 0)
        {
            return HitExpectedAfterSeqIds[0];
        }

        if (hitIndex >= HitExpectedAfterSeqIds.Count)
        {
            return HitExpectedAfterSeqIds[HitExpectedAfterSeqIds.Count - 1];
        }

        return HitExpectedAfterSeqIds[hitIndex];
    }

    public double GetHitAngleDegrees(int hitIndex)
    {
        if (HitAnglesDegrees.Count == 0)
        {
            return double.NaN;
        }

        if (hitIndex < 0)
        {
            return HitAnglesDegrees[0];
        }

        if (hitIndex >= HitAnglesDegrees.Count)
        {
            return HitAnglesDegrees[HitAnglesDegrees.Count - 1];
        }

        return HitAnglesDegrees[hitIndex];
    }
}
