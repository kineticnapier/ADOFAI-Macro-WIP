using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Macro_Inserter;

// Runtime compatibility patch for the current InternalMacroService implementation.
// It replaces BuildInputPlan and TryFirePseudoChordGroup without requiring a full
// InternalMacroService.cs replacement.
internal static class PseudoChordInputPlanFix
{
    private static readonly Harmony Harmony = new("Macro-Inserter.PseudoChordInputPlanFix.v3");
    private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
    private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
    private static readonly FieldInfo? DirectHitInvokerField = AccessTools.Field(typeof(InternalMacroService), "directHitInvoker");
    private static readonly MethodInfo? TryReadCurrentFloorSeqIdMethod = AccessTools.Method(typeof(InternalMacroService), "TryReadCurrentFloorSeqId");
    private static readonly MethodInfo? LogHitResultMethod = AccessTools.Method(typeof(InternalMacroService), "LogHitResult");
    private static readonly MethodInfo? RecordHitDiffMethod = AccessTools.Method(typeof(InternalMacroService), "RecordHitDiff");
    private static readonly MethodInfo? PulseMacroKeyViewerMethod = AccessTools.Method(typeof(InternalMacroService), "PulseMacroKeyViewer");

    private static bool patched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void RuntimeInitialize()
    {
        Install("RuntimeInitializeOnLoadMethod");
    }

    internal static void Install(string reason)
    {
        if (patched)
        {
            return;
        }

        try
        {
            MethodInfo? buildOriginal = AccessTools.Method(
                typeof(InternalMacroService),
                "BuildInputPlan",
                new[] { typeof(IReadOnlyList<MacroPlanEntry>) });
            MethodInfo? buildPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(BuildInputPlanPrefix));

            MethodInfo? fireOriginal = AccessTools.Method(
                typeof(InternalMacroService),
                "TryFirePseudoChordGroup",
                new[]
                {
                    typeof(InputPlanEntry),
                    typeof(double),
                    typeof(double),
                    typeof(double),
                    typeof(int),
                    typeof(int),
                    typeof(int).MakeByRefType()
                });
            MethodInfo? firePrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(TryFirePseudoChordGroupPrefix));

            if (buildOriginal == null || buildPrefix == null || fireOriginal == null || firePrefix == null)
            {
                Debug.Log("[Macro-Inserter] PseudoChordInputPlanFix not installed: target method not found.");
                return;
            }

            Harmony.Patch(buildOriginal, prefix: new HarmonyMethod(buildPrefix));
            Harmony.Patch(fireOriginal, prefix: new HarmonyMethod(firePrefix));
            patched = true;
            Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix installed by {reason}.");
        }
        catch (Exception ex)
        {
            Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix install failed: {ex}");
        }
    }

    private static bool BuildInputPlanPrefix(
        InternalMacroService __instance,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        ref IReadOnlyList<InputPlanEntry> __result)
    {
        InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
        Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
        if (settings == null)
        {
            return true;
        }

        __result = BuildInputPlan(macroPlan, settings, log);
        return false;
    }

    private static bool TryFirePseudoChordGroupPrefix(
        InternalMacroService __instance,
        InputPlanEntry entry,
        double clockSeconds,
        double effectiveTargetTimeSeconds,
        double diffMs,
        int currentFloorBefore,
        int dueCount,
        ref int currentFloorAfter,
        ref bool __result)
    {
        DirectHitInvoker? directHitInvoker = DirectHitInvokerField?.GetValue(__instance) as DirectHitInvoker;
        Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
        if (directHitInvoker == null)
        {
            return true;
        }

        currentFloorAfter = currentFloorBefore;
        int acceptedHitCount = 0;
        bool completed = true;

        for (int hitIndex = 0; hitIndex < entry.EmittedHitCount; hitIndex++)
        {
            int beforeFloorSeqId = currentFloorAfter;
            if (TryReadCurrentFloorSeqId(__instance, out int verifiedBeforeFloorSeqId))
            {
                beforeFloorSeqId = verifiedBeforeFloorSeqId;
            }

            if (beforeFloorSeqId >= entry.LastSeqId)
            {
                currentFloorAfter = beforeFloorSeqId;
                break;
            }

            int targetSeqId = Math.Min(beforeFloorSeqId + 1, entry.LastSeqId);
            double hitTargetTimeSeconds = entry.GetHitTargetTimeSeconds(hitIndex);
            HitInvokeResult result = directHitInvoker.Invoke(
                targetSeqId,
                clockSeconds,
                beforeFloorSeqId,
                hitTargetTimeSeconds,
                forceReadAfterHit: true);
            InvokeLogHitResult(__instance, beforeFloorSeqId, result);

            currentFloorAfter = result.AfterFloorSeqId;
            if (currentFloorAfter < 0 && TryReadCurrentFloorSeqId(__instance, out int verifiedAfterFloorSeqId))
            {
                currentFloorAfter = verifiedAfterFloorSeqId;
            }

            if (!result.Accepted)
            {
                completed = false;
                break;
            }

            acceptedHitCount++;
            InvokeRecordHitDiff(__instance, (clockSeconds - hitTargetTimeSeconds) * 1000.0);
            InvokePulseMacroKeyViewer(__instance);
        }

        log?.Invoke(
            $"pseudoChord duplicate-cluster compressed. groupStartIndex={entry.PlanStartIndex} groupEndIndex={entry.PlanEndIndexExclusive - 1} firstSeqID={entry.FirstSeqId} lastSeqID={entry.LastSeqId} seqID={entry.FirstSeqId}-{entry.LastSeqId} firstTargetTime={entry.FirstTargetTimeSeconds:F6}s lastTargetTime={entry.LastTargetTimeSeconds:F6}s rawEntryCount={entry.RawEntryCount} emittedHitCount={entry.EmittedHitCount} acceptedHitCount={acceptedHitCount} windowMs=<patched> spanMs={entry.SpanMs:F3} currentFloorBefore={currentFloorBefore} currentFloorAfter={currentFloorAfter} dueCount={dueCount} containsMidspin={entry.ContainsMidspin} hitTargetTimes={FormatHitTargetTimes(entry)}");

        __result = completed && (acceptedHitCount == entry.EmittedHitCount || currentFloorAfter >= entry.LastSeqId);
        return false;
    }

    private static IReadOnlyList<InputPlanEntry> BuildInputPlan(
        IReadOnlyList<MacroPlanEntry> macroPlan,
        InternalMacroSettings settings,
        Action<string>? log)
    {
        if (macroPlan.Count == 0)
        {
            return Array.Empty<InputPlanEntry>();
        }

        double windowMs = Math.Max(0.0, settings.PseudoChordWindowMs);
        double configuredMaxSpanMs = Math.Max(0.0, settings.PseudoChordMaxSpanMs);
        double maxSpanMs = configuredMaxSpanMs > 0.0
            ? Math.Min(windowMs, configuredMaxSpanMs)
            : windowMs;
        double exactDuplicateEpsilonMs = Math.Max(0.0, settings.PseudoChordExactDuplicateEpsilonMs);
        int maxHitsPerGroup = Math.Max(1, settings.MaxHitsPerPseudoChordGroup);
        int keyCapacity = GetPseudoChordKeyCapacity(settings, maxHitsPerGroup);
        List<InputPlanEntry> entries = new();

        int index = 0;
        while (index < macroPlan.Count)
        {
            int startIndex = index;
            MacroPlanEntry first = macroPlan[startIndex];
            double firstTargetTimeSeconds = first.TargetTimeSeconds;
            MacroPlanEntry last = first;
            bool containsMidspin = first.IsMidspin;
            bool isNearMidspin = first.IsNearMidspin;
            index++;

            while (index < macroPlan.Count)
            {
                MacroPlanEntry candidate = macroPlan[index];
                double spanMs = (candidate.TargetTimeSeconds - firstTargetTimeSeconds) * 1000.0;
                bool isExactDuplicateOfFirst = Math.Abs(spanMs) <= exactDuplicateEpsilonMs;
                if (!isExactDuplicateOfFirst && spanMs > windowMs)
                {
                    break;
                }

                last = candidate;
                containsMidspin |= candidate.IsMidspin;
                isNearMidspin |= candidate.IsNearMidspin;
                index++;
            }

            int rawEntryCount = index - startIndex;
            if (rawEntryCount == 1)
            {
                AddSingleInputPlanEntry(entries, macroPlan, startIndex);
                continue;
            }

            double actualSpanMs = (last.TargetTimeSeconds - firstTargetTimeSeconds) * 1000.0;
            if (actualSpanMs > maxSpanMs + 0.001)
            {
                Log(log, $"pseudoChord rejected: span exceeds window. groupStartIndex={startIndex} groupEndIndex={index - 1} firstSeqID={first.SeqId} lastSeqID={last.SeqId} spanMs={actualSpanMs:F3} windowMs={windowMs:F3} maxSpanMs={maxSpanMs:F3}");
                AddSingleInputPlanEntry(entries, macroPlan, startIndex);
                index = startIndex + 1;
                continue;
            }

            IReadOnlyList<double> clusterTargetTimes = BuildExactTimeClusters(macroPlan, startIndex, index, exactDuplicateEpsilonMs);
            int emittedHitCount = Math.Min(clusterTargetTimes.Count, Math.Min(keyCapacity, maxHitsPerGroup));
            bool isCompressed = emittedHitCount < rawEntryCount;

            if (!isCompressed)
            {
                Log(log, $"pseudoChord passthrough expanded. groupStartIndex={startIndex} groupEndIndex={index - 1} firstSeqID={first.SeqId} lastSeqID={last.SeqId} firstTargetTime={first.TargetTimeSeconds:F6}s lastTargetTime={last.TargetTimeSeconds:F6}s rawEntryCount={rawEntryCount} emittedIndividualEntries={rawEntryCount} clusterCount={clusterTargetTimes.Count} windowMs={windowMs:F3} exactDuplicateEpsilonMs={exactDuplicateEpsilonMs:F3} spanMs={actualSpanMs:F3} reason=no-duplicate-cluster-compression");
                AddIndividualInputPlanEntries(entries, macroPlan, startIndex, index);
                continue;
            }

            List<double> emittedTargetTimes = clusterTargetTimes.Take(emittedHitCount).ToList();
            entries.Add(new InputPlanEntry(
                startIndex,
                index,
                first.SeqId,
                last.SeqId,
                first.TargetTimeSeconds,
                last.TargetTimeSeconds,
                rawEntryCount,
                Math.Max(1, emittedHitCount),
                containsMidspin,
                isNearMidspin,
                isExactDuplicateGroup: actualSpanMs <= exactDuplicateEpsilonMs + 0.000001,
                isCompressed: true,
                hitTargetTimeSeconds: emittedTargetTimes));

            Log(log, $"pseudoChord duplicate-cluster planned. groupStartIndex={startIndex} groupEndIndex={index - 1} firstSeqID={first.SeqId} lastSeqID={last.SeqId} firstTargetTime={first.TargetTimeSeconds:F6}s lastTargetTime={last.TargetTimeSeconds:F6}s rawEntryCount={rawEntryCount} emittedHitCount={emittedHitCount} clusterCount={clusterTargetTimes.Count} windowMs={windowMs:F3} exactDuplicateEpsilonMs={exactDuplicateEpsilonMs:F3} spanMs={actualSpanMs:F3} hitTargetTimes={FormatHitTargetTimes(emittedTargetTimes)}");
        }

        return entries;
    }

    private static IReadOnlyList<double> BuildExactTimeClusters(
        IReadOnlyList<MacroPlanEntry> macroPlan,
        int startIndex,
        int endIndexExclusive,
        double exactDuplicateEpsilonMs)
    {
        List<double> targetTimes = new();
        if (startIndex >= endIndexExclusive)
        {
            return targetTimes;
        }

        double currentClusterTime = macroPlan[startIndex].TargetTimeSeconds;
        targetTimes.Add(currentClusterTime);
        for (int index = startIndex + 1; index < endIndexExclusive; index++)
        {
            double candidateTime = macroPlan[index].TargetTimeSeconds;
            double deltaMs = Math.Abs(candidateTime - currentClusterTime) * 1000.0;
            if (deltaMs <= exactDuplicateEpsilonMs)
            {
                continue;
            }

            currentClusterTime = candidateTime;
            targetTimes.Add(currentClusterTime);
        }

        return targetTimes;
    }

    private static int GetPseudoChordKeyCapacity(InternalMacroSettings settings, int fallback)
    {
        int viewerKeyCount = MacroKeyViewerState.ParseKeyNames(settings.MacroKeyViewerKeysText).Length;
        return Math.Max(
            1,
            Math.Max(
                Math.Max(1, settings.VirtualInputKeyCount),
                viewerKeyCount > 0 ? viewerKeyCount : fallback));
    }

    private static void AddIndividualInputPlanEntries(
        List<InputPlanEntry> entries,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        int startIndex,
        int endIndexExclusive)
    {
        for (int planIndex = startIndex; planIndex < endIndexExclusive; planIndex++)
        {
            AddSingleInputPlanEntry(entries, macroPlan, planIndex);
        }
    }

    private static void AddSingleInputPlanEntry(
        List<InputPlanEntry> entries,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        int planIndex)
    {
        MacroPlanEntry macroEntry = macroPlan[planIndex];
        entries.Add(new InputPlanEntry(
            planIndex,
            planIndex + 1,
            macroEntry.SeqId,
            macroEntry.SeqId,
            macroEntry.TargetTimeSeconds,
            macroEntry.TargetTimeSeconds,
            rawEntryCount: 1,
            emittedHitCount: 1,
            containsMidspin: macroEntry.IsMidspin,
            isNearMidspin: macroEntry.IsNearMidspin,
            isExactDuplicateGroup: false,
            isCompressed: false));
    }

    private static bool TryReadCurrentFloorSeqId(InternalMacroService instance, out int currentFloorSeqId)
    {
        currentFloorSeqId = -1;
        if (TryReadCurrentFloorSeqIdMethod == null)
        {
            return false;
        }

        object[] args = { currentFloorSeqId };
        object? result = TryReadCurrentFloorSeqIdMethod.Invoke(instance, args);
        if (args.Length > 0 && args[0] is int readFloor)
        {
            currentFloorSeqId = readFloor;
        }

        return result is bool boolResult && boolResult;
    }

    private static void InvokeLogHitResult(InternalMacroService instance, int currentFloorSeqId, HitInvokeResult result)
    {
        LogHitResultMethod?.Invoke(instance, new object[] { currentFloorSeqId, result });
    }

    private static void InvokeRecordHitDiff(InternalMacroService instance, double diffMs)
    {
        RecordHitDiffMethod?.Invoke(instance, new object[] { diffMs });
    }

    private static void InvokePulseMacroKeyViewer(InternalMacroService instance)
    {
        PulseMacroKeyViewerMethod?.Invoke(instance, Array.Empty<object>());
    }

    private static void Log(Action<string>? log, string message)
    {
        log?.Invoke(message);
    }

    private static string FormatHitTargetTimes(InputPlanEntry entry)
    {
        return FormatHitTargetTimes(entry.HitTargetTimeSeconds);
    }

    private static string FormatHitTargetTimes(IReadOnlyList<double> targetTimes)
    {
        if (targetTimes.Count == 0)
        {
            return "<none>";
        }

        return string.Join(",", targetTimes.Select(time => time.ToString("F6")));
    }
}
