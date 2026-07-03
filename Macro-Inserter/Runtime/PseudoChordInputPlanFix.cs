using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Macro_Inserter;

// Compatibility patch for the existing InternalMacroService implementation.
// Gameplay compression is intentionally disabled: every MacroPlanEntry/floor must
// remain hittable, otherwise the scheduler can jump from 1842 to 1845 while the
// game is still on floor 1842.
internal static class PseudoChordInputPlanFix
{
    private static readonly Harmony Harmony = new("Macro-Inserter.PseudoChordInputPlanFix.v4");
    private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
    private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
    private static readonly FieldInfo? PlanField = AccessTools.Field(typeof(InternalMacroService), "plan");
    private static readonly FieldInfo? AdaptiveOffsetMsField = AccessTools.Field(typeof(InternalMacroService), "adaptiveOffsetMs");
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
                Debug.Log("[Macro-Inserter] PseudoChordInputPlanFix v4 not installed: target method not found.");
                return;
            }

            Harmony.Patch(buildOriginal, prefix: new HarmonyMethod(buildPrefix));
            Harmony.Patch(fireOriginal, prefix: new HarmonyMethod(firePrefix));
            patched = true;
            Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v4 installed by {reason}.");
        }
        catch (Exception ex)
        {
            Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v4 install failed: {ex}");
        }
    }

    private static bool BuildInputPlanPrefix(
        InternalMacroService __instance,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        ref IReadOnlyList<InputPlanEntry> __result)
    {
        Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
        List<InputPlanEntry> entries = new(macroPlan.Count);

        for (int index = 0; index < macroPlan.Count; index++)
        {
            MacroPlanEntry macroEntry = macroPlan[index];
            entries.Add(new InputPlanEntry(
                index,
                index + 1,
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

        __result = entries;
        log?.Invoke($"PseudoChordInputPlanFix v4 expanded input plan without gameplay compression. macroEntries={macroPlan.Count} inputEntries={entries.Count}");
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
        IReadOnlyList<MacroPlanEntry>? macroPlan = PlanField?.GetValue(__instance) as IReadOnlyList<MacroPlanEntry>;
        InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
        if (directHitInvoker == null || macroPlan == null)
        {
            return true;
        }

        currentFloorAfter = currentFloorBefore;
        int acceptedHitCount = 0;
        bool completed = true;

        int startIndex = Math.Max(0, entry.PlanStartIndex);
        int endIndex = Math.Min(entry.PlanEndIndexExclusive, macroPlan.Count);
        double adaptiveSeconds = GetAdaptiveOffsetSeconds(settings, __instance);

        for (int planIndex = startIndex; planIndex < endIndex; planIndex++)
        {
            MacroPlanEntry macroEntry = macroPlan[planIndex];
            int beforeFloorSeqId = currentFloorAfter;
            if (TryReadCurrentFloorSeqId(__instance, out int verifiedBeforeFloorSeqId))
            {
                beforeFloorSeqId = verifiedBeforeFloorSeqId;
            }

            if (beforeFloorSeqId >= macroEntry.SeqId)
            {
                currentFloorAfter = beforeFloorSeqId;
                continue;
            }

            if (beforeFloorSeqId < macroEntry.SeqId - 1)
            {
                completed = false;
                currentFloorAfter = beforeFloorSeqId;
                log?.Invoke($"pseudoChord passthrough stopped: floor not ready inside group. currentFloor={beforeFloorSeqId} targetSeqID={macroEntry.SeqId} groupSeqID={entry.FirstSeqId}-{entry.LastSeqId}");
                break;
            }

            double hitTargetTimeSeconds = macroEntry.TargetTimeSeconds + adaptiveSeconds;
            HitInvokeResult result = directHitInvoker.Invoke(
                macroEntry.SeqId,
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

            if (!result.Accepted || currentFloorAfter < macroEntry.SeqId)
            {
                completed = false;
                break;
            }

            acceptedHitCount++;
            InvokeRecordHitDiff(__instance, (clockSeconds - hitTargetTimeSeconds) * 1000.0);
            InvokePulseMacroKeyViewer(__instance);
        }

        log?.Invoke(
            $"pseudoChord passthrough fired. groupStartIndex={entry.PlanStartIndex} groupEndIndex={entry.PlanEndIndexExclusive - 1} firstSeqID={entry.FirstSeqId} lastSeqID={entry.LastSeqId} rawEntryCount={entry.RawEntryCount} acceptedHitCount={acceptedHitCount} currentFloorBefore={currentFloorBefore} currentFloorAfter={currentFloorAfter} dueCount={dueCount} compression=off");

        __result = completed && currentFloorAfter >= entry.LastSeqId;
        return false;
    }

    private static double GetAdaptiveOffsetSeconds(InternalMacroSettings? settings, InternalMacroService instance)
    {
        if (settings == null || !settings.EnableAdaptiveOffset)
        {
            return 0.0;
        }

        object? raw = AdaptiveOffsetMsField?.GetValue(instance);
        return raw is double adaptiveMs ? adaptiveMs / 1000.0 : 0.0;
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
}
