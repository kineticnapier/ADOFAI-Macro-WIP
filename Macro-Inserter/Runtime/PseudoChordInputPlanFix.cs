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
        private const string PatchId = "Macro-Inserter.PseudoChordInputPlanFix.v6";
        private const double AngleEqualEpsilonDegrees = 0.001;

        private static readonly Harmony Harmony = new(PatchId);
        private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
        private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
        private static readonly FieldInfo? PlanField = AccessTools.Field(typeof(InternalMacroService), "plan");
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
                if (!buildPatched)
                {
                    MethodInfo? buildOriginal = AccessTools.Method(
                        typeof(InternalMacroService),
                        "BuildInputPlan",
                        new[] { typeof(IReadOnlyList<MacroPlanEntry>) });
                    MethodInfo? buildPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(BuildInputPlanPrefix));

                    if (buildOriginal != null && buildPrefix != null)
                    {
                        Harmony.Patch(buildOriginal, prefix: new HarmonyMethod(buildPrefix));
                        buildPatched = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v6 build patch failed: {ex}");
            }

            try
            {
                if (!firePatched)
                {
                    MethodInfo? fireOriginal = FindTryFirePseudoChordGroupMethod();
                    MethodInfo? firePrefix = null;
                    int parameterCount = fireOriginal?.GetParameters().Length ?? 0;
                    if (parameterCount == 5)
                    {
                        firePrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(TryFirePseudoChordGroupPrefixCurrent));
                    }
                    else if (parameterCount == 7)
                    {
                        firePrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(TryFirePseudoChordGroupPrefixLegacy));
                    }

                    if (fireOriginal != null && firePrefix != null)
                    {
                        Harmony.Patch(fireOriginal, prefix: new HarmonyMethod(firePrefix));
                        firePatched = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v6 fire patch failed: {ex}");
            }

            if (!installAttempted || buildPatched || firePatched)
            {
                installAttempted = true;
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v6 install by {reason}. buildPatched={buildPatched} firePatched={firePatched}");
            }
        }

        private static MethodInfo? FindTryFirePseudoChordGroupMethod()
        {
            return typeof(InternalMacroService)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(method => method.Name == "TryFirePseudoChordGroup")
                .OrderBy(method => method.GetParameters().Length == 5 ? 0 : 1)
                .FirstOrDefault();
        }

        private static bool BuildInputPlanPrefix(
            InternalMacroService __instance,
            IReadOnlyList<MacroPlanEntry> macroPlan,
            ref IReadOnlyList<InputPlanEntry> __result)
        {
            Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
            InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
            List<InputPlanEntry> entries = new();

            if (macroPlan.Count == 0)
            {
                __result = Array.Empty<InputPlanEntry>();
                return false;
            }

            double windowMs = Math.Max(0.0, settings?.PseudoChordWindowMs ?? 0.0);
            double configuredMaxSpanMs = Math.Max(0.0, settings?.PseudoChordMaxSpanMs ?? 0.0);
            double maxSpanMs = configuredMaxSpanMs > 0.0
                ? Math.Min(windowMs, configuredMaxSpanMs)
                : windowMs;
            double exactDuplicateEpsilonMs = Math.Max(0.0, settings?.PseudoChordExactDuplicateEpsilonMs ?? 0.05);

            int index = 0;
            int compressedGroupCount = 0;
            int compressedRawCount = 0;
            int compressedHitCount = 0;

            while (index < macroPlan.Count)
            {
                int startIndex = index;
                MacroPlanEntry first = macroPlan[startIndex];
                int endIndex = startIndex + 1;
                MacroPlanEntry last = first;
                bool containsMidspin = first.IsMidspin;
                bool isNearMidspin = first.IsNearMidspin;

                if (windowMs > 0.0)
                {
                    while (endIndex < macroPlan.Count)
                    {
                        MacroPlanEntry candidate = macroPlan[endIndex];
                        double spanMs = (candidate.TargetTimeSeconds - first.TargetTimeSeconds) * 1000.0;
                        if (spanMs > windowMs)
                        {
                            break;
                        }

                        last = candidate;
                        containsMidspin |= candidate.IsMidspin;
                        isNearMidspin |= candidate.IsNearMidspin;
                        endIndex++;
                    }
                }

                int rawEntryCount = endIndex - startIndex;
                double actualSpanMs = (last.TargetTimeSeconds - first.TargetTimeSeconds) * 1000.0;
                bool validSpan = rawEntryCount > 1 && actualSpanMs <= maxSpanMs + 0.001;
                bool shouldTryMidspinCompression = validSpan && containsMidspin;

                if (shouldTryMidspinCompression &&
                    TryBuildMidspinCompressedEntry(
                        macroPlan,
                        startIndex,
                        endIndex,
                        exactDuplicateEpsilonMs,
                        out InputPlanEntry? compressedEntry,
                        out string compressionReason) &&
                    compressedEntry != null)
                {
                    entries.Add(compressedEntry);
                    compressedGroupCount++;
                    compressedRawCount += compressedEntry.RawEntryCount;
                    compressedHitCount += compressedEntry.EmittedHitCount;
                    log?.Invoke(
                        $"pseudoChord angle-midspin planned. groupStartIndex={startIndex} groupEndIndex={endIndex - 1} seqID={compressedEntry.FirstSeqId}-{compressedEntry.LastSeqId} rawEntryCount={compressedEntry.RawEntryCount} emittedHitCount={compressedEntry.EmittedHitCount} spanMs={compressedEntry.SpanMs:F3} reason={compressionReason}");
                    index = endIndex;
                    continue;
                }

                // If no safe midspin compression is available, do not keep a near-time group.
                // Every floor remains an individual scheduler entry.
                AddIndividualEntries(entries, macroPlan, startIndex, Math.Max(startIndex + 1, endIndex));
                index = Math.Max(startIndex + 1, endIndex);
            }

            __result = entries;
            log?.Invoke(
                $"PseudoChordInputPlanFix v6 built angle/midspin input plan. macroEntries={macroPlan.Count} inputEntries={entries.Count} compressedGroups={compressedGroupCount} compressedRaw={compressedRawCount} compressedHits={compressedHitCount}");
            return false;
        }

        private static bool TryBuildMidspinCompressedEntry(
            IReadOnlyList<MacroPlanEntry> macroPlan,
            int startIndex,
            int endIndex,
            double exactDuplicateEpsilonMs,
            out InputPlanEntry? entry,
            out string reason)
        {
            entry = null;
            reason = "not-compressible";

            MacroPlanEntry first = macroPlan[startIndex];
            MacroPlanEntry last = macroPlan[endIndex - 1];
            int rawEntryCount = endIndex - startIndex;
            List<double> hitTargetTimes = new();
            List<int> hitExpectedAfterSeqIds = new();
            List<double> hitAngles = new();
            int midspinCount = 0;
            int nonMidspinCount = 0;

            for (int planIndex = startIndex; planIndex < endIndex; planIndex++)
            {
                MacroPlanEntry macroEntry = macroPlan[planIndex];
                if (macroEntry.IsMidspin)
                {
                    midspinCount++;
                    continue;
                }

                nonMidspinCount++;
                hitTargetTimes.Add(macroEntry.TargetTimeSeconds);
                hitExpectedAfterSeqIds.Add(macroEntry.SeqId);
                hitAngles.Add(macroEntry.AngleDegrees ?? double.NaN);
            }

            if (midspinCount == 0 || nonMidspinCount == 0 || nonMidspinCount >= rawEntryCount)
            {
                reason = $"midspinCount={midspinCount} nonMidspinCount={nonMidspinCount} rawEntryCount={rawEntryCount}";
                return false;
            }

            // If the last floor is a midspin marker, the final real hit may need to advance
            // through it. Keep the emitted-hit count unchanged, but require the last hit to
            // confirm at least the group end when possible.
            if (last.IsMidspin && hitExpectedAfterSeqIds.Count > 0)
            {
                int lastIndex = hitExpectedAfterSeqIds.Count - 1;
                hitExpectedAfterSeqIds[lastIndex] = Math.Max(hitExpectedAfterSeqIds[lastIndex], last.SeqId);
            }

            bool hasDifferentAnglesAtSameTime = HasDifferentAnglesAtSameTime(macroPlan, startIndex, endIndex, exactDuplicateEpsilonMs);
            reason = hasDifferentAnglesAtSameTime
                ? $"skip-midspin-keep-angle-slots midspinCount={midspinCount} nonMidspinCount={nonMidspinCount} differentAnglesAtSameTime=True"
                : $"skip-midspin midspinCount={midspinCount} nonMidspinCount={nonMidspinCount}";

            entry = new InputPlanEntry(
                startIndex,
                endIndex,
                first.SeqId,
                last.SeqId,
                first.TargetTimeSeconds,
                last.TargetTimeSeconds,
                rawEntryCount,
                hitTargetTimes.Count,
                isExactDuplicateGroup: false,
                containsMidspin: true,
                isNearMidspin: true,
                isCompressed: true,
                hitTargetTimeSeconds: hitTargetTimes,
                hitExpectedAfterSeqIds: hitExpectedAfterSeqIds,
                hitAnglesDegrees: hitAngles);
            return true;
        }

        private static bool HasDifferentAnglesAtSameTime(
            IReadOnlyList<MacroPlanEntry> macroPlan,
            int startIndex,
            int endIndex,
            double exactDuplicateEpsilonMs)
        {
            for (int left = startIndex; left < endIndex; left++)
            {
                MacroPlanEntry a = macroPlan[left];
                if (!a.AngleDegrees.HasValue)
                {
                    continue;
                }

                for (int right = left + 1; right < endIndex; right++)
                {
                    MacroPlanEntry b = macroPlan[right];
                    if (!b.AngleDegrees.HasValue)
                    {
                        continue;
                    }

                    double timeDiffMs = Math.Abs(a.TargetTimeSeconds - b.TargetTimeSeconds) * 1000.0;
                    if (timeDiffMs > exactDuplicateEpsilonMs)
                    {
                        continue;
                    }

                    if (Math.Abs(NormalizeAngleDelta(a.AngleDegrees.Value - b.AngleDegrees.Value)) > AngleEqualEpsilonDegrees)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static double NormalizeAngleDelta(double value)
        {
            double delta = value % 360.0;
            if (delta > 180.0)
            {
                delta -= 360.0;
            }
            else if (delta < -180.0)
            {
                delta += 360.0;
            }

            return delta;
        }

        private static void AddIndividualEntries(
            List<InputPlanEntry> entries,
            IReadOnlyList<MacroPlanEntry> macroPlan,
            int startIndex,
            int endIndex)
        {
            for (int planIndex = startIndex; planIndex < endIndex; planIndex++)
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
                    isExactDuplicateGroup: false,
                    containsMidspin: macroEntry.IsMidspin,
                    isNearMidspin: macroEntry.IsNearMidspin,
                    isCompressed: false));
            }
        }

        private static bool TryFirePseudoChordGroupPrefixCurrent(
            InternalMacroService __instance,
            InputPlanEntry entry,
            double clockSeconds,
            int currentFloorBefore,
            int dueCount,
            ref int currentFloorAfter,
            ref bool __result)
        {
            return TryFirePseudoChordGroupCore(__instance, entry, clockSeconds, currentFloorBefore, dueCount, ref currentFloorAfter, ref __result);
        }

        private static bool TryFirePseudoChordGroupPrefixLegacy(
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
            return TryFirePseudoChordGroupCore(__instance, entry, clockSeconds, currentFloorBefore, dueCount, ref currentFloorAfter, ref __result);
        }

        private static bool TryFirePseudoChordGroupCore(
            InternalMacroService instance,
            InputPlanEntry entry,
            double clockSeconds,
            int currentFloorBefore,
            int dueCount,
            ref int currentFloorAfter,
            ref bool result)
        {
            DirectHitInvoker? directHitInvoker = DirectHitInvokerField?.GetValue(instance) as DirectHitInvoker;
            Action<string>? log = LogField?.GetValue(instance) as Action<string>;
            IReadOnlyList<MacroPlanEntry>? macroPlan = PlanField?.GetValue(instance) as IReadOnlyList<MacroPlanEntry>;
            InternalMacroSettings? settings = SettingsField?.GetValue(instance) as InternalMacroSettings;
            if (directHitInvoker == null || macroPlan == null)
            {
                return true;
            }

            currentFloorAfter = currentFloorBefore;
            int acceptedHitCount = 0;
            int skippedAlreadyPastHitCount = 0;
            bool completed = true;
            double adaptiveSeconds = GetAdaptiveOffsetSeconds(settings, instance);
            int emittedHitCount = Math.Max(1, entry.EmittedHitCount);

            for (int hitIndex = 0; hitIndex < emittedHitCount; hitIndex++)
            {
                int beforeFloorSeqId = currentFloorAfter;
                if (TryReadCurrentFloorSeqId(instance, out int verifiedBeforeFloorSeqId))
                {
                    beforeFloorSeqId = verifiedBeforeFloorSeqId;
                }

                currentFloorAfter = beforeFloorSeqId;
                int expectedAfterSeqId = entry.GetHitExpectedAfterSeqId(hitIndex);
                if (beforeFloorSeqId >= expectedAfterSeqId)
                {
                    skippedAlreadyPastHitCount++;
                    continue;
                }

                if (beforeFloorSeqId < entry.FirstSeqId - 1)
                {
                    completed = false;
                    log?.Invoke($"pseudoChord v6 stopped: floor not ready before group. currentFloor={beforeFloorSeqId} targetSeqID={entry.FirstSeqId}-{entry.LastSeqId} hitIndex={hitIndex} expectedAfter={expectedAfterSeqId}");
                    break;
                }

                int targetSeqId = Math.Min(beforeFloorSeqId + 1, entry.LastSeqId);
                double hitTargetTimeSeconds = entry.GetHitTargetTimeSeconds(hitIndex) + adaptiveSeconds;
                double hitAngle = entry.GetHitAngleDegrees(hitIndex);
                HitInvokeResult hitResult = directHitInvoker.Invoke(
                    targetSeqId,
                    clockSeconds,
                    beforeFloorSeqId,
                    hitTargetTimeSeconds,
                    forceReadAfterHit: true);
                InvokeLogHitResult(instance, beforeFloorSeqId, hitResult);

                currentFloorAfter = hitResult.AfterFloorSeqId;
                if (currentFloorAfter < 0 && TryReadCurrentFloorSeqId(instance, out int verifiedAfterFloorSeqId))
                {
                    currentFloorAfter = verifiedAfterFloorSeqId;
                }

                if (!hitResult.Accepted)
                {
                    completed = false;
                    log?.Invoke($"pseudoChord v6 stopped: DirectHit rejected. hitIndex={hitIndex} targetSeqID={targetSeqId} expectedAfter={expectedAfterSeqId} angle={FormatAngle(hitAngle)} beforeFloor={beforeFloorSeqId} afterFloor={currentFloorAfter}");
                    break;
                }

                acceptedHitCount++;
                InvokeRecordHitDiff(instance, (clockSeconds - hitTargetTimeSeconds) * 1000.0);
                InvokePulseMacroKeyViewer(instance);

                if (currentFloorAfter < expectedAfterSeqId)
                {
                    completed = false;
                    log?.Invoke($"pseudoChord v6 stopped: floor advance short. hitIndex={hitIndex} targetSeqID={targetSeqId} expectedAfter={expectedAfterSeqId} angle={FormatAngle(hitAngle)} beforeFloor={beforeFloorSeqId} afterFloor={currentFloorAfter}");
                    break;
                }
            }

            log?.Invoke(
                $"pseudoChord angle-midspin fired. groupStartIndex={entry.PlanStartIndex} groupEndIndex={entry.PlanEndIndexExclusive - 1} firstSeqID={entry.FirstSeqId} lastSeqID={entry.LastSeqId} rawEntryCount={entry.RawEntryCount} emittedHitCount={entry.EmittedHitCount} acceptedHitCount={acceptedHitCount} skippedAlreadyPast={skippedAlreadyPastHitCount} currentFloorBefore={currentFloorBefore} currentFloorAfter={currentFloorAfter} dueCount={dueCount} containsMidspin={entry.ContainsMidspin} compressed={entry.IsCompressed}");

            result = completed && (acceptedHitCount + skippedAlreadyPastHitCount >= emittedHitCount || currentFloorAfter >= entry.LastSeqId);
            return false;
        }

        private static string FormatAngle(double angle)
        {
            return double.IsNaN(angle) ? "<unknown>" : angle.ToString("F3");
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
