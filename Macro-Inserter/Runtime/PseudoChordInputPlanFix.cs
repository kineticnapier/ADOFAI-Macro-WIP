using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace Macro_Inserter
{
    internal static class PseudoChordInputPlanFix
    {
        private const double DefaultPseudoChordWindowMs = 2.0;
        private const double MaxMidspinChainSpanMs = 16.0;

        private static readonly Harmony Harmony = new("Macro-Inserter.PseudoChordInputPlanFix.v5");
        private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
        private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
        private static readonly FieldInfo? AdaptiveOffsetMsField = AccessTools.Field(typeof(InternalMacroService), "adaptiveOffsetMs");
        private static readonly FieldInfo? DirectHitInvokerField = AccessTools.Field(typeof(InternalMacroService), "directHitInvoker");
        private static readonly MethodInfo? TryReadCurrentFloorSeqIdMethod = AccessTools.Method(typeof(InternalMacroService), "TryReadCurrentFloorSeqId");
        private static readonly MethodInfo? LogHitResultMethod = AccessTools.Method(typeof(InternalMacroService), "LogHitResult");
        private static readonly MethodInfo? RecordHitDiffMethod = AccessTools.Method(typeof(InternalMacroService), "RecordHitDiff");
        private static readonly MethodInfo? PulseMacroKeyViewerMethod = AccessTools.Method(typeof(InternalMacroService), "PulseMacroKeyViewer");

        private static bool buildPatched;
        private static bool firePatched;
        private static bool installAttempted;

        [ModuleInitializer]
        internal static void ModuleInitialize()
        {
            Install("ModuleInitializer");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RuntimeInitialize()
        {
            Install("RuntimeInitializeOnLoadMethod");
        }

        internal static void Install(string reason)
        {
            if (buildPatched && firePatched)
            {
                return;
            }

            try
            {
                MethodInfo? buildOriginal = FindBuildInputPlanMethod();
                MethodInfo? buildPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(BuildInputPlanPrefix));
                if (!buildPatched && buildOriginal != null && buildPrefix != null)
                {
                    Harmony.Patch(buildOriginal, prefix: new HarmonyMethod(buildPrefix));
                    buildPatched = true;
                }

                MethodInfo? fireOriginal = FindTryFirePseudoChordGroupMethod();
                MethodInfo? firePrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(TryFirePseudoChordGroupPrefix));
                if (!firePatched && fireOriginal != null && firePrefix != null)
                {
                    Harmony.Patch(fireOriginal, prefix: new HarmonyMethod(firePrefix));
                    firePatched = true;
                }

                if (!installAttempted || buildPatched || firePatched)
                {
                    Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v5 installed by {reason}. buildPatched={buildPatched} firePatched={firePatched}");
                }

                installAttempted = true;
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v5 install failed: {ex}");
            }
        }

        private static MethodInfo? FindBuildInputPlanMethod()
        {
            return typeof(InternalMacroService)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    if (method.Name != "BuildInputPlan")
                    {
                        return false;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 1;
                });
        }

        private static MethodInfo? FindTryFirePseudoChordGroupMethod()
        {
            return typeof(InternalMacroService)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name == "TryFirePseudoChordGroup");
        }

        private static bool BuildInputPlanPrefix(
            InternalMacroService __instance,
            IReadOnlyList<MacroPlanEntry> macroPlan,
            ref IReadOnlyList<InputPlanEntry> __result)
        {
            Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
            InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
            double chainWindowSeconds = GetWindowMs(settings) / 1000.0;
            List<InputPlanEntry> entries = new(macroPlan.Count);

            int index = 0;
            int compressedGroups = 0;
            int compressedRawEntries = 0;
            int compressedEmittedHits = 0;

            while (index < macroPlan.Count)
            {
                MacroPlanEntry first = macroPlan[index];
                int startIndex = index;
                int endIndex = CollectMidspinCompressionGroup(macroPlan, startIndex, chainWindowSeconds);
                int rawEntryCount = endIndex - startIndex;

                if (rawEntryCount <= 1)
                {
                    AddSingle(entries, startIndex, first);
                    index++;
                    continue;
                }

                int midspinCount = 0;
                bool containsNearMidspin = false;
                List<double> emittedTargetTimes = new();
                for (int planIndex = startIndex; planIndex < endIndex; planIndex++)
                {
                    MacroPlanEntry candidate = macroPlan[planIndex];
                    if (candidate.IsMidspin)
                    {
                        midspinCount++;
                    }
                    else
                    {
                        emittedTargetTimes.Add(candidate.TargetTimeSeconds);
                    }

                    containsNearMidspin |= candidate.IsNearMidspin;
                }

                int emittedHitCount = emittedTargetTimes.Count;
                if (midspinCount <= 0 || emittedHitCount <= 0 || emittedHitCount >= rawEntryCount)
                {
                    for (int planIndex = startIndex; planIndex < endIndex; planIndex++)
                    {
                        AddSingle(entries, planIndex, macroPlan[planIndex]);
                    }

                    index = endIndex;
                    continue;
                }

                MacroPlanEntry last = macroPlan[endIndex - 1];
                entries.Add(new InputPlanEntry(
                    startIndex,
                    endIndex,
                    first.SeqId,
                    last.SeqId,
                    first.TargetTimeSeconds,
                    last.TargetTimeSeconds,
                    rawEntryCount,
                    emittedHitCount,
                    containsMidspin: true,
                    isNearMidspin: containsNearMidspin,
                    isExactDuplicateGroup: false,
                    isCompressed: true,
                    hitTargetTimeSeconds: emittedTargetTimes));

                compressedGroups++;
                compressedRawEntries += rawEntryCount;
                compressedEmittedHits += emittedHitCount;
                log?.Invoke(
                    $"pseudoChord midspin-compressed planned. groupStartIndex={startIndex} groupEndIndex={endIndex - 1} firstSeqID={first.SeqId} lastSeqID={last.SeqId} rawEntryCount={rawEntryCount} midspinCount={midspinCount} emittedHitCount={emittedHitCount} spanMs={(last.TargetTimeSeconds - first.TargetTimeSeconds) * 1000.0:F3} windowMs={GetWindowMs(settings):F3}");

                index = endIndex;
            }

            __result = entries;
            log?.Invoke(
                $"PseudoChordInputPlanFix v5 built input plan. macroEntries={macroPlan.Count} inputEntries={entries.Count} compressedGroups={compressedGroups} compressedRawEntries={compressedRawEntries} compressedEmittedHits={compressedEmittedHits}");
            return false;
        }

        private static int CollectMidspinCompressionGroup(
            IReadOnlyList<MacroPlanEntry> macroPlan,
            int startIndex,
            double chainWindowSeconds)
        {
            MacroPlanEntry first = macroPlan[startIndex];
            MacroPlanEntry previous = first;
            bool containsMidspin = first.IsMidspin;
            int index = startIndex + 1;

            while (index < macroPlan.Count)
            {
                MacroPlanEntry candidate = macroPlan[index];
                double gapSeconds = candidate.TargetTimeSeconds - previous.TargetTimeSeconds;
                double spanMs = (candidate.TargetTimeSeconds - first.TargetTimeSeconds) * 1000.0;
                bool linksToMidspinRun = containsMidspin || previous.IsMidspin || candidate.IsMidspin || candidate.IsNearMidspin || previous.IsNearMidspin;

                if (gapSeconds < -0.000001 || gapSeconds > chainWindowSeconds || spanMs > MaxMidspinChainSpanMs || !linksToMidspinRun)
                {
                    break;
                }

                containsMidspin |= candidate.IsMidspin;
                previous = candidate;
                index++;
            }

            if (!containsMidspin)
            {
                return startIndex + 1;
            }

            return index;
        }

        private static void AddSingle(List<InputPlanEntry> entries, int planIndex, MacroPlanEntry macroEntry)
        {
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

        private static double GetWindowMs(InternalMacroSettings? settings)
        {
            if (settings == null)
            {
                return DefaultPseudoChordWindowMs;
            }

            double configured = Math.Max(0.0, settings.PseudoChordWindowMs);
            return configured > 0.0 ? configured : DefaultPseudoChordWindowMs;
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
            if (!entry.IsCompressed)
            {
                return true;
            }

            DirectHitInvoker? directHitInvoker = DirectHitInvokerField?.GetValue(__instance) as DirectHitInvoker;
            Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
            InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
            if (directHitInvoker == null)
            {
                return true;
            }

            currentFloorAfter = currentFloorBefore;
            int acceptedHitCount = 0;
            bool completed = true;
            double adaptiveSeconds = GetAdaptiveOffsetSeconds(settings, __instance);

            for (int hitIndex = 0; hitIndex < entry.EmittedHitCount; hitIndex++)
            {
                int beforeFloorSeqId = currentFloorAfter;
                if (TryReadCurrentFloorSeqId(__instance, out int verifiedBeforeFloorSeqId))
                {
                    beforeFloorSeqId = verifiedBeforeFloorSeqId;
                }

                if (beforeFloorSeqId >= entry.LastSeqId)
                {
                    break;
                }

                int targetSeqId = Math.Min(beforeFloorSeqId + 1, entry.LastSeqId);
                double hitTargetTimeSeconds = entry.GetHitTargetTimeSeconds(hitIndex) + adaptiveSeconds;
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
                $"pseudoChord midspin-compressed fired. groupStartIndex={entry.PlanStartIndex} groupEndIndex={entry.PlanEndIndexExclusive - 1} firstSeqID={entry.FirstSeqId} lastSeqID={entry.LastSeqId} rawEntryCount={entry.RawEntryCount} emittedHitCount={entry.EmittedHitCount} acceptedHitCount={acceptedHitCount} currentFloorBefore={currentFloorBefore} currentFloorAfter={currentFloorAfter} dueCount={dueCount} spanMs={entry.SpanMs:F3}");

            __result = completed && acceptedHitCount == entry.EmittedHitCount;
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
}
