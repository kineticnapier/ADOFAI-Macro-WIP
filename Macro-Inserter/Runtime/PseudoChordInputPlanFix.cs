using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;

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
        private static readonly Harmony Harmony = new Harmony("Macro-Inserter.PseudoChordInputPlanFix.v43");
        private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
        private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
        private static readonly FieldInfo? InputPlanField = AccessTools.Field(typeof(InternalMacroService), "inputPlan");
        private static readonly FieldInfo? NextIndexField = AccessTools.Field(typeof(InternalMacroService), "nextIndex");

        private const int BurstDueCountThreshold = int.MaxValue;
        private const int BurstKeyCountThreshold = int.MaxValue;
        private const int MaxBurstEntries = 4096;
        private const int MaxBurstKeys = 4096;

        private static int directKeyTimesEntriesSinceSummary;
        private static int directKeyTimesKeysSinceSummary;
        private static int directKeyTimesFloorAdvanceSinceSummary;
        private static int macroKeyViewerPulsesSinceSummary;
        private static long macroKeyViewerFallbackCounter;
        private static double lastDirectKeyTimesSummaryRealtime;
        private static int stuckPlainSingleSeqId = -1;
        private static int stuckPlainSingleAttemptCount;
        private static double lastDirectKeyTimesSpikeLogRealtime = -10.0;
        private static int directKeyTimesSpikeLogSuppressed;

        private static bool patched;
        private static bool patchAttempted;

        [System.Runtime.CompilerServices.ModuleInitializer]
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
            if (patched)
            {
                return;
            }

            if (patchAttempted && reason == "InputPlanEntry static constructor")
            {
                return;
            }

            patchAttempted = true;
            try
            {
                MethodInfo? buildOriginal = AccessTools.Method(
                    typeof(InternalMacroService),
                    "BuildInputPlan",
                    new[] { typeof(IReadOnlyList<MacroPlanEntry>) });
                MethodInfo? buildPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(BuildInputPlanPrefix));

                MethodInfo? firePrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(TryFirePseudoChordGroupPrefix));
                IReadOnlyList<MethodInfo> fireOriginals = AccessTools.GetDeclaredMethods(typeof(InternalMacroService))
                    .Where(method => method.Name == "TryFirePseudoChordGroup")
                    .ToArray();

                NaturalFingeringOptions.Load();
                MacroKeyViewerRainOverlay.EnsureInstalled();

                bool buildPatched = false;
                bool firePatched = false;
                if (buildOriginal != null && buildPrefix != null)
                {
                    Harmony.Patch(buildOriginal, prefix: new HarmonyMethod(buildPrefix));
                    buildPatched = true;
                }

                if (firePrefix != null)
                {
                    foreach (MethodInfo fireOriginal in fireOriginals)
                    {
                        Harmony.Patch(fireOriginal, prefix: new HarmonyMethod(firePrefix));
                        firePatched = true;
                    }
                }

                patched = buildPatched && firePatched;
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v43 installed by {reason}. buildPatched={buildPatched} firePatched={firePatched} rainOverlay=True uiPatchDisabled=True loadPatchDisabled=True logPatchDisabled=True");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v43 install failed: {ex}");
            }
        }

        private static bool BuildInputPlanPrefix(
            InternalMacroService __instance,
            IReadOnlyList<MacroPlanEntry> macroPlan,
            ref IReadOnlyList<InputPlanEntry> __result)
        {
            InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
            Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
            if (settings == null || log == null)
            {
                return true;
            }

            if (ChartFileInputPlanBuilder.TryBuild(settings, log, macroPlan, out IReadOnlyList<InputPlanEntry> runtimeInputPlan))
            {
                // The runtime input-pipeline plan is executed through the original
                // DirectHit branch because that is the only branch that calls
                // TryFirePseudoChordGroup(). The v11/v12/v13/v14/v15/v17/v18/v20/v21/v23/v24/v25/v26/v27/v28/v29/v30/v31/v33/v34/v35/v36 prefix on that method
                // intercepts the call and schedules InputPatchState instead of
                // calling scrController.Hit() directly.
                //
                // If the UI is left on FireMode.InputPatch, the original
                // InputPatch branch bypasses TryFirePseudoChordGroup(), ignores
                // InputPlanEntry.EmittedHitCount, uses settings.VirtualInputKeyCount,
                // and advances nextIndex without confirming floor movement. That
                // makes the scheduler desync and appear to stop.
                if (settings.FireMode != FireMode.DirectHit)
                {
                    LogMinimal(log, $"Runtime input-pipeline plan active; forcing FireMode DirectHit branch. selectedFireMode={settings.FireMode} actualInjection=DirectKeyTimes");
                    settings.FireMode = FireMode.DirectHit;
                }

                if (!settings.EnableHighDensityMode)
                {
                    settings.EnableHighDensityMode = true;
                    LogMinimal(log, "Runtime input-pipeline plan active; forcing EnableHighDensityMode=True so DirectKeyTimes can keep up with dense input sections.");
                }

                if (settings.MaxHitsPerPlayerControlUpdate < 5000)
                {
                    int previousMaxHits = settings.MaxHitsPerPlayerControlUpdate;
                    settings.MaxHitsPerPlayerControlUpdate = 5000;
                    LogMinimal(log, $"Runtime input-pipeline plan active; raising MaxHitsPerPlayerControlUpdate from {previousMaxHits} to 5000 for dense directKeyTimes sections.");
                }

                __result = runtimeInputPlan;
                return false;
            }

            LogMinimal(log, "Runtime input-pipeline plan unavailable; disabling internal input plan instead of falling back to DirectHit.");
            __result = Array.Empty<InputPlanEntry>();
            return false;
        }

        private static bool TryFirePseudoChordGroupPrefix(
            InternalMacroService __instance,
            InputPlanEntry entry,
            double clockSeconds,
            int currentFloorBefore,
            int dueCount,
            ref int currentFloorAfter,
            ref bool __result)
        {
            if (!entry.UseInputPatchPipeline)
            {
                return true;
            }

            Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
            int keyCount = Math.Max(1, entry.EmittedHitCount);

            object? controller = ReflectionCache.GetSingletonInstance("scrController");
            bool asyncInputActive = ReflectionCache.TryReadBool("AsyncInputManager", out bool active, "isActive") && active;
            double prefixStartRealtime = Time.realtimeSinceStartupAsDouble;
            long prefixStartMemory = Profiler.GetTotalAllocatedMemoryLong();

            // v17 proved that faking ValidInputWasTriggered/CountValidKeysPressed is
            // unstable here: the synthetic hit window can be consumed by unrelated Up
            // events, and AsyncInput may skip the normal PlayerControl_Update body.
            // Inject directly at the game's real input queue instead:
            //   keyTimes.Add(now) x keyCount
            //   UpdateHoldKeys -> Hit(false)
            // HitInputEvent(false, Down) is still accepted by InputPatches while the
            // synthetic hit window is open.
            //
            // v21 tried to use Hit(false) directly for every plain single. The logs
            // showed that it advanced currentFloor, but the game could still die
            // around 180-degree turns because this bypasses too much of the normal
            // keyTimes/update path. v24 keeps directKeyTimes as the primary path for
            // every entry, and only uses DirectHit as a delayed emergency retry for a
            // plain single that is already stuck. v25 adds a burst drain path for
            // sections where thousands of floor events per second make one-prefix-per-entry
            // scheduling too expensive. v26 raises burst caps and gives stalled
            // bursts more simulated updates before falling back. v27 tried a last-resort
            // direct-hit finish after burst drain, but that can corrupt sections
            // that already worked through the normal input queue. v28 keeps the
            // dense KV throttling, removes the direct-hit finish, and only enables
            // burst mode for genuinely dense key bursts. v29 disables burst execution again for stability comparisons and keeps only the safer KV throttling. v30 changes only MacroKeyViewer fingering: beat-bank key assignment, while gameplay input stays directKeyTimes. v31 fixes the fallback MacroKeyViewer counter compile error and maps row-major 24-key layouts to logical hand/foot banks. v33 reverses left-side bank order so left banks play inside-to-outside while right banks keep display order. v34 uses the finest visual beat grid that stays at or below 1000 BPM. v35 keeps downward folding at <=1000 but only raises low BPM sections up to <=500. v36 adds capped natural-fingering debug logs for overflow/expansion buckets. v37 adds adjustable visual BPM thresholds, a clean UI/log mode patch, lag-spike diagnostics, and a MacroKeyViewer rain view. v40 also replaces the UnityModManager OnGUI delegate from Main.Load postfix because Harmony-patching Main.OnGUI alone can leave UMM using the original delegate.
            bool plainSingle =
                keyCount == 1 &&
                entry.RawEntryCount == 1 &&
                !entry.ContainsMidspin &&
                !entry.IsCompressed;

            if (TryBuildDueBurst(
                    __instance,
                    dueCount,
                    out int burstEntryCount,
                    out int burstKeyCount,
                    out int burstTargetSeqId))
            {
                int burstAfterFloor = DirectKeyTimesInputInjector.InjectBurst(
                    controller,
                    burstKeyCount,
                    burstTargetSeqId,
                    forceSimulation: asyncInputActive,
                    maxSimulationSteps: Math.Min(MaxBurstKeys + 64, Math.Max(1, burstKeyCount) + 64),
                    log);

                if (burstAfterFloor >= entry.FirstSeqId)
                {
                    ResetStuckPlainSingle(entry.FirstSeqId);
                    currentFloorAfter = burstAfterFloor;
                    PulseMacroKeyViewer(__instance, entry, burstKeyCount);
                    RecordDirectKeyTimesSummary(log, burstKeyCount, currentFloorBefore, burstAfterFloor, asyncInputActive);
                    LogDirectKeyTimesSpikeIfNeeded(__instance, log, "burst", prefixStartRealtime, prefixStartMemory, keyCount, burstKeyCount, dueCount, currentFloorBefore, burstAfterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive);
                    if (burstAfterFloor < burstTargetSeqId)
                    {
                        LogNormal(log, $"directKeyTimes burst partial. dueCount={dueCount} burstEntries={burstEntryCount} burstKeys={burstKeyCount} targetAfter={burstTargetSeqId} currentFloorBefore={currentFloorBefore} afterFloor={burstAfterFloor}");
                    }

                    __result = true;
                    return false;
                }

                LogNormal(log, $"directKeyTimes burst did not reach first target. dueCount={dueCount} burstEntries={burstEntryCount} burstKeys={burstKeyCount} targetAfter={burstTargetSeqId} currentFloorBefore={currentFloorBefore} afterFloor={burstAfterFloor}");
            }

            int afterFloor = DirectKeyTimesInputInjector.Inject(
                controller,
                keyCount,
                forceSimulation: asyncInputActive,
                allowDirectHitFallback: false,
                log);

            if (afterFloor >= entry.FirstSeqId)
            {
                ResetStuckPlainSingle(entry.FirstSeqId);
                currentFloorAfter = afterFloor;
                PulseMacroKeyViewer(__instance, entry, keyCount);
                RecordDirectKeyTimesSummary(log, keyCount, currentFloorBefore, afterFloor, asyncInputActive);
                LogDirectKeyTimesSpikeIfNeeded(__instance, log, "directKeyTimes", prefixStartRealtime, prefixStartMemory, keyCount, keyCount, dueCount, currentFloorBefore, afterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive);
                __result = true;
                return false;
            }

            if (plainSingle)
            {
                int attempts = RegisterStuckPlainSingle(entry.FirstSeqId);
                double lateMs = Math.Max(0.0, (clockSeconds - entry.FirstTargetTimeSeconds) * 1000.0);
                if (attempts >= 3 || lateMs >= 60.0)
                {
                    int fallbackAfterFloor = DirectKeyTimesInputInjector.InvokeDirectHitOnly(
                        controller,
                        syntheticHitBudget: 2,
                        log);
                    LogNormal(log,
                        $"plainSingle delayed DirectHit fallback tried. seqID={entry.FirstSeqId} attempts={attempts} lateMs={lateMs:F3} currentFloorBefore={currentFloorBefore} afterFloor={fallbackAfterFloor}");

                    if (fallbackAfterFloor >= entry.FirstSeqId)
                    {
                        ResetStuckPlainSingle(entry.FirstSeqId);
                        currentFloorAfter = fallbackAfterFloor;
                        PulseMacroKeyViewer(__instance, entry, keyCount);
                        RecordDirectKeyTimesSummary(log, keyCount, currentFloorBefore, fallbackAfterFloor, asyncInputActive);
                        LogDirectKeyTimesSpikeIfNeeded(__instance, log, "plainSingleDirectHitFallback", prefixStartRealtime, prefixStartMemory, keyCount, keyCount, dueCount, currentFloorBefore, fallbackAfterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive);
                        __result = true;
                        return false;
                    }
                }
            }
            else
            {
                ResetStuckPlainSingle(entry.FirstSeqId);
            }

            currentFloorAfter = currentFloorBefore;
            LogNormal(log,
                $"directKeyTimes queued; waiting for floor confirmation. keyCount={keyCount} seqID={entry.FirstSeqId}-{entry.LastSeqId} targetTime={entry.FirstTargetTimeSeconds:F6}s spanMs={entry.SpanMs:F3} rawEntryCount={entry.RawEntryCount} containsMidspin={entry.ContainsMidspin} isNearMidspin={entry.IsNearMidspin} plainSingle={plainSingle} stuckPlainSingleAttempts={stuckPlainSingleAttemptCount} currentFloorBefore={currentFloorBefore} currentFloorAfter={afterFloor} expectedAfter={entry.LastSeqId} dueCount={dueCount} asyncInputActive={asyncInputActive}");
            LogDirectKeyTimesSpikeIfNeeded(__instance, log, "queuedNoAdvance", prefixStartRealtime, prefixStartMemory, keyCount, keyCount, dueCount, currentFloorBefore, afterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive);
            __result = false;
            return false;
        }

        private static bool TryBuildDueBurst(
            InternalMacroService service,
            int dueCount,
            out int burstEntryCount,
            out int burstKeyCount,
            out int burstTargetSeqId)
        {
            burstEntryCount = 0;
            burstKeyCount = 0;
            burstTargetSeqId = -1;

            if (dueCount < BurstDueCountThreshold)
            {
                return false;
            }

            if (InputPlanField?.GetValue(service) is not IReadOnlyList<InputPlanEntry> inputPlan ||
                NextIndexField?.GetValue(service) is not int nextIndex ||
                nextIndex < 0 ||
                nextIndex >= inputPlan.Count)
            {
                return false;
            }

            int maxEntries = Math.Min(Math.Min(dueCount, MaxBurstEntries), inputPlan.Count - nextIndex);
            for (int i = 0; i < maxEntries; i++)
            {
                InputPlanEntry burstEntry = inputPlan[nextIndex + i];
                if (!burstEntry.UseInputPatchPipeline)
                {
                    break;
                }

                int entryKeyCount = Math.Max(1, burstEntry.EmittedHitCount);
                if (burstEntryCount > 0 && burstKeyCount + entryKeyCount > MaxBurstKeys)
                {
                    break;
                }

                burstEntryCount++;
                burstKeyCount += entryKeyCount;
                burstTargetSeqId = Math.Max(burstTargetSeqId, burstEntry.LastSeqId);
            }

            return burstEntryCount >= BurstDueCountThreshold && burstKeyCount >= BurstKeyCountThreshold && burstTargetSeqId > 0;
        }

        private static int RegisterStuckPlainSingle(int seqId)
        {
            if (stuckPlainSingleSeqId == seqId)
            {
                stuckPlainSingleAttemptCount++;
            }
            else
            {
                stuckPlainSingleSeqId = seqId;
                stuckPlainSingleAttemptCount = 1;
            }

            return stuckPlainSingleAttemptCount;
        }

        private static void ResetStuckPlainSingle(int seqId)
        {
            if (stuckPlainSingleSeqId == seqId || seqId < 0)
            {
                stuckPlainSingleSeqId = -1;
                stuckPlainSingleAttemptCount = 0;
            }
        }

        private static void PulseMacroKeyViewer(InternalMacroService service, InputPlanEntry entry, int keyCount)
        {
            InternalMacroSettings? settings = SettingsField?.GetValue(service) as InternalMacroSettings;
            if (settings == null || !settings.EnableMacroKeyViewer || keyCount <= 0)
            {
                return;
            }

            IReadOnlyList<string> configuredKeys = service.MacroKeyViewer.ConfigureKeys(settings.MacroKeyViewerKeysText);
            if (configuredKeys.Count == 0 && entry.AssignedKeyNames.Count == 0)
            {
                return;
            }

            // v36: the game input path still only queues keyTimes. Natural fingering is
            // a MacroKeyViewer layer: pulse the beat-bank assigned key names instead of
            // the old round-robin counter. Keep v29's UI load cap so dense sections do
            // not destabilize playback.
            if (keyCount >= 128)
            {
                return;
            }

            const int maxPulsesPerSummaryWindow = 64;
            if (macroKeyViewerPulsesSinceSummary >= maxPulsesPerSummaryWindow)
            {
                return;
            }

            double durationSeconds = Math.Max(0, settings.MacroKeyViewerPulseMs) / 1000.0;
            int remainingPulseBudget = maxPulsesPerSummaryWindow - macroKeyViewerPulsesSinceSummary;
            int pulseCount = Math.Min(Math.Min(keyCount, 32), remainingPulseBudget);
            for (int i = 0; i < pulseCount; i++)
            {
                string keyName;
                if (entry.AssignedKeyNames.Count > 0)
                {
                    keyName = entry.AssignedKeyNames[i % entry.AssignedKeyNames.Count];
                }
                else
                {
                    int keyIndex = (int)(macroKeyViewerFallbackCounter % configuredKeys.Count);
                    macroKeyViewerFallbackCounter++;
                    keyName = configuredKeys[keyIndex];
                }

                try
                {
                    service.MacroKeyViewer.Pulse(keyName, durationSeconds);
                    macroKeyViewerPulsesSinceSummary++;
                }
                catch
                {
                    break;
                }
            }
        }

        private static void RecordDirectKeyTimesSummary(
            Action<string>? log,
            int keyCount,
            int beforeFloor,
            int afterFloor,
            bool asyncInputActive)
        {
            directKeyTimesEntriesSinceSummary++;
            directKeyTimesKeysSinceSummary += Math.Max(1, keyCount);
            if (afterFloor > beforeFloor)
            {
                directKeyTimesFloorAdvanceSinceSummary += afterFloor - beforeFloor;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (lastDirectKeyTimesSummaryRealtime <= 0.0)
            {
                lastDirectKeyTimesSummaryRealtime = now;
                return;
            }

            double elapsed = now - lastDirectKeyTimesSummaryRealtime;
            if (elapsed < 0.5)
            {
                return;
            }

            double keysPerSecond = directKeyTimesKeysSinceSummary / Math.Max(0.001, elapsed);
            LogMinimal(
                log,
                $"directKeyTimes summary. entries={directKeyTimesEntriesSinceSummary} keys={directKeyTimesKeysSinceSummary} floorAdvance={directKeyTimesFloorAdvanceSinceSummary} macroKeyViewerPulses={macroKeyViewerPulsesSinceSummary} elapsedMs={elapsed * 1000.0:F1} approxKps={keysPerSecond:F1} asyncInputActive={asyncInputActive}");

            directKeyTimesEntriesSinceSummary = 0;
            directKeyTimesKeysSinceSummary = 0;
            directKeyTimesFloorAdvanceSinceSummary = 0;
            macroKeyViewerPulsesSinceSummary = 0;
            lastDirectKeyTimesSummaryRealtime = now;
        }
        private static void LogDirectKeyTimesSpikeIfNeeded(
            InternalMacroService service,
            Action<string>? log,
            string operation,
            double startRealtime,
            long startMemoryBytes,
            int entryKeyCount,
            int processedKeyCount,
            int dueCount,
            int beforeFloor,
            int afterFloor,
            int firstSeqId,
            int lastSeqId,
            double targetTimeSeconds,
            double clockSeconds,
            bool asyncInputActive)
        {
            NaturalFingeringOptions.Load();
            if (!NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Minimal))
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            double processingMs = (now - startRealtime) * 1000.0;
            double lateMs = Math.Max(0.0, (clockSeconds - targetTimeSeconds) * 1000.0);
            bool processingSpike = NaturalFingeringOptions.EnableLagSpikeLog &&
                                   processingMs >= Math.Max(0.1, NaturalFingeringOptions.LagSpikeLogMs);
            bool lateSpike = NaturalFingeringOptions.EnableLateSpikeLog &&
                             lateMs >= Math.Max(0.1, NaturalFingeringOptions.LateSpikeLogMs);
            if (!processingSpike && !lateSpike)
            {
                return;
            }

            double minIntervalSeconds = Math.Max(0.0, NaturalFingeringOptions.SpikeLogMinIntervalMs) / 1000.0;
            if (minIntervalSeconds > 0.0 && now - lastDirectKeyTimesSpikeLogRealtime < minIntervalSeconds)
            {
                directKeyTimesSpikeLogSuppressed++;
                return;
            }

            int suppressed = directKeyTimesSpikeLogSuppressed;
            directKeyTimesSpikeLogSuppressed = 0;
            lastDirectKeyTimesSpikeLogRealtime = now;

            long memoryDeltaBytes = Profiler.GetTotalAllocatedMemoryLong() - startMemoryBytes;
            string reason = processingSpike && lateSpike
                ? "late+processing"
                : lateSpike
                    ? "late"
                    : "processing";

            LogMinimal(
                log,
                $"directKeyTimes spike v43. reason={reason} op={operation} lateMs={lateMs:F3} lateThresholdMs={NaturalFingeringOptions.LateSpikeLogMs:F3} processingMs={processingMs:F3} processingThresholdMs={NaturalFingeringOptions.LagSpikeLogMs:F3} clock={clockSeconds:F6}s target={targetTimeSeconds:F6}s entryKeys={entryKeyCount} processedKeys={processedKeyCount} dueCount={dueCount} floor={beforeFloor}->{afterFloor} seqID={firstSeqId}-{lastSeqId} asyncInputActive={asyncInputActive} macroKeyViewerPulsesWindow={macroKeyViewerPulsesSinceSummary} memDeltaKB={memoryDeltaBytes / 1024.0:F1} suppressedSinceLast={suppressed}");

            if (NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Normal))
            {
                string context = BuildInputPlanContext(service, clockSeconds);
                LogNormal(log, $"directKeyTimes spike context. {context}");
            }

            if (NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Verbose))
            {
                string current = BuildCurrentEntryDebug(firstSeqId, targetTimeSeconds, clockSeconds, entryKeyCount, processedKeyCount, dueCount);
                LogVerbose(log, $"directKeyTimes spike current. {current}");
            }
        }

        private static string BuildCurrentEntryDebug(
            int firstSeqId,
            double targetTimeSeconds,
            double clockSeconds,
            int entryKeyCount,
            int processedKeyCount,
            int dueCount)
        {
            double lateMs = Math.Max(0.0, (clockSeconds - targetTimeSeconds) * 1000.0);
            return $"seqID={firstSeqId} target={targetTimeSeconds:F6}s clock={clockSeconds:F6}s lateMs={lateMs:F3} entryKeys={entryKeyCount} processedKeys={processedKeyCount} dueCount={dueCount}";
        }

        private static string BuildInputPlanContext(InternalMacroService service, double clockSeconds)
        {
            if (InputPlanField?.GetValue(service) is not IReadOnlyList<InputPlanEntry> inputPlan || inputPlan.Count == 0)
            {
                return "inputPlan=<unavailable>";
            }

            int nextIndex = NextIndexField?.GetValue(service) is int readNextIndex ? readNextIndex : -1;
            if (nextIndex < 0)
            {
                return $"inputPlanCount={inputPlan.Count} nextIndex=<unavailable>";
            }

            int start = Math.Max(0, nextIndex - 2);
            int end = Math.Min(inputPlan.Count - 1, nextIndex + 3);
            List<string> parts = new List<string>();
            for (int i = start; i <= end; i++)
            {
                InputPlanEntry entry = inputPlan[i];
                double lateMs = Math.Max(0.0, (clockSeconds - entry.FirstTargetTimeSeconds) * 1000.0);
                string assigned = entry.AssignedKeyNames.Count == 0
                    ? "-"
                    : string.Join(",", entry.AssignedKeyNames.Take(8).ToArray()) + (entry.AssignedKeyNames.Count > 8 ? ",..." : string.Empty);
                parts.Add(
                    $"#{i}{(i == nextIndex ? "*" : string.Empty)}:seq={entry.FirstSeqId}-{entry.LastSeqId},target={entry.FirstTargetTimeSeconds:F6},lateMs={lateMs:F2},keys={entry.EmittedHitCount},raw={entry.RawEntryCount},spanMs={entry.SpanMs:F2},assigned={assigned}");
            }

            return $"inputPlanCount={inputPlan.Count} nextIndex={nextIndex} window=[{string.Join(" | ", parts.ToArray())}]";
        }

        private static void LogMinimal(Action<string>? log, string message)
        {
            if (NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Minimal))
            {
                log?.Invoke(message);
            }
        }

        private static void LogNormal(Action<string>? log, string message)
        {
            if (NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Normal))
            {
                log?.Invoke(message);
            }
        }

        private static void LogVerbose(Action<string>? log, string message)
        {
            if (NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Verbose))
            {
                log?.Invoke(message);
            }
        }


    }
}
