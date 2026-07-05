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
        private static readonly Harmony Harmony = new Harmony("Macro-Inserter.PseudoChordInputPlanFix.v52");
        private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
        private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
        private static readonly FieldInfo? InputPlanField = AccessTools.Field(typeof(InternalMacroService), "inputPlan");
        private static readonly FieldInfo? NextIndexField = AccessTools.Field(typeof(InternalMacroService), "nextIndex");

        private const int BurstDueCountThreshold = int.MaxValue;
        private const int BurstKeyCountThreshold = int.MaxValue;
        private const int MaxBurstEntries = 4096;
        private const int MaxBurstKeys = 4096;

        private const int DeferredTraceCapacity = 64;

        private static int directKeyTimesEntriesSinceSummary;
        private static int directKeyTimesKeysSinceSummary;
        private static int directKeyTimesFloorAdvanceSinceSummary;
        private static int macroKeyViewerPulsesSinceSummary;
        private static long macroKeyViewerFallbackCounter;
        private static double lastDirectKeyTimesSummaryRealtime;
        private static int stuckPlainSingleSeqId = -1;
        private static int stuckPlainSingleAttemptCount;
        private static int lastQueueOnlyKeyViewerPulseSeqId = -1;
        private static readonly DeferredDirectKeyTimesTrace[] DeferredTraceRing = new DeferredDirectKeyTimesTrace[DeferredTraceCapacity];
        private static int deferredTraceWriteIndex;
        private static int deferredTraceCount;
        private static int deferredTraceTotalCount;
        private static int deferredLateSpikeCount;
        private static int deferredProcessingSpikeCount;
        private static int deferredQueuedNoAdvanceCount;
        private static int deferredMaxDueCount;
        private static double deferredMaxLateMs;
        private static double deferredMaxProcessingMs;
        private static double deferredMaxDeltaMs;
        private static int deferredMaxAfterFloor;

        private struct DirectKeyTimesTrace
        {
            private readonly double startRealtime;

            public DirectKeyTimesTrace()
            {
                startRealtime = Time.realtimeSinceStartupAsDouble;
                StartFrame = Time.frameCount;
                StartDeltaTimeMs = Time.deltaTime * 1000.0f;
                StartUnscaledDeltaTimeMs = Time.unscaledDeltaTime * 1000.0f;
            }

            public int StartFrame { get; }
            public int EndFrame { get; private set; }
            public float StartDeltaTimeMs { get; }
            public float StartUnscaledDeltaTimeMs { get; }
            public float EndDeltaTimeMs { get; private set; }
            public float EndUnscaledDeltaTimeMs { get; private set; }
            public double SetupMs { get; private set; }
            public double DirectCallMs { get; private set; }
            public double FloorCheckMs { get; private set; }
            public double KeyViewerNotifyMs { get; private set; }
            public double SummaryMs { get; private set; }
            public double SpikeBookkeepingMs { get; private set; }
            public int KeyViewerPulseCount { get; set; }
            public bool KeyViewerEnabled { get; set; }
            public bool RainEnabled { get; set; }

            public void MarkSetupDone()
            {
                SetupMs = ElapsedMs();
            }

            public double Begin()
            {
                return Time.realtimeSinceStartupAsDouble;
            }

            public void AddDirectCall(double marker)
            {
                DirectCallMs += Since(marker);
            }

            public void AddFloorCheck(double marker)
            {
                FloorCheckMs += Since(marker);
            }

            public void AddKeyViewerNotify(double marker)
            {
                KeyViewerNotifyMs += Since(marker);
            }

            public void AddSummary(double marker)
            {
                SummaryMs += Since(marker);
            }

            public void AddSpikeBookkeeping(double marker)
            {
                SpikeBookkeepingMs += Since(marker);
            }

            public double TotalMs => ElapsedMs();

            public void Finish()
            {
                EndFrame = Time.frameCount;
                EndDeltaTimeMs = Time.deltaTime * 1000.0f;
                EndUnscaledDeltaTimeMs = Time.unscaledDeltaTime * 1000.0f;
            }

            private double ElapsedMs()
            {
                return (Time.realtimeSinceStartupAsDouble - startRealtime) * 1000.0;
            }

            private static double Since(double marker)
            {
                return (Time.realtimeSinceStartupAsDouble - marker) * 1000.0;
            }
        }

        private sealed class DeferredDirectKeyTimesTrace
        {
            public string Operation { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public double ClockSeconds { get; set; }
            public double TargetTimeSeconds { get; set; }
            public double LateMs { get; set; }
            public double ProcessingMs { get; set; }
            public int EntryKeyCount { get; set; }
            public int ProcessedKeyCount { get; set; }
            public int DueCount { get; set; }
            public int BeforeFloor { get; set; }
            public int AfterFloor { get; set; }
            public int FirstSeqId { get; set; }
            public int LastSeqId { get; set; }
            public bool AsyncInputActive { get; set; }
            public int StartFrame { get; set; }
            public int EndFrame { get; set; }
            public float StartDeltaTimeMs { get; set; }
            public float EndDeltaTimeMs { get; set; }
            public float StartUnscaledDeltaTimeMs { get; set; }
            public float EndUnscaledDeltaTimeMs { get; set; }
            public double SetupMs { get; set; }
            public double DirectCallMs { get; set; }
            public double FloorCheckMs { get; set; }
            public double KeyViewerNotifyMs { get; set; }
            public double SummaryMs { get; set; }
            public double TotalMs { get; set; }
            public bool KeyViewerEnabled { get; set; }
            public bool RainEnabled { get; set; }
            public int KeyViewerPulseCount { get; set; }
            public int MacroKeyViewerPulsesWindow { get; set; }
            public double MemoryDeltaKb { get; set; }
        }

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

                MethodInfo? effectiveTargetOriginal = AccessTools.Method(
                    typeof(InternalMacroService),
                    "GetEffectiveTargetTimeSeconds",
                    new[] { typeof(InputPlanEntry) });
                MethodInfo? effectiveTargetPostfix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(GetEffectiveInputTargetTimePostfix));

                NaturalFingeringOptions.Load();
                MacroKeyViewerRainOverlay.EnsureInstalled();

                bool buildPatched = false;
                bool firePatched = false;
                bool queueLeadPatched = false;
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

                if (effectiveTargetOriginal != null && effectiveTargetPostfix != null)
                {
                    Harmony.Patch(effectiveTargetOriginal, postfix: new HarmonyMethod(effectiveTargetPostfix));
                    queueLeadPatched = true;
                }

                patched = buildPatched && firePatched;
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v52 installed by {reason}. buildPatched={buildPatched} firePatched={firePatched} queueLeadPatched={queueLeadPatched} rainOverlay=True uiPatchDisabled=True loadPatchDisabled=True logPatchDisabled=True");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v52 install failed: {ex}");
            }
        }

        private static void GetEffectiveInputTargetTimePostfix(
            InternalMacroService __instance,
            InputPlanEntry entry,
            ref double __result)
        {
            InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
            if (settings == null ||
                !settings.EnableCameraSafeMode ||
                !settings.CameraSafeQueueOnlyMode ||
                settings.CameraSafeQueueLeadMs <= 0.0)
            {
                return;
            }

            // v51: queue-only avoids forced Simulated_PlayerControl_Update, but the
            // game's normal update consumes the queued keyTimes one frame later.
            // Apply a queue-only lead to scheduling, separate from global offset/
            // play-correction, so the camera-safe path does not lean late/right.
            __result = Math.Max(0.0, __result - settings.CameraSafeQueueLeadMs / 1000.0);
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

            Action<string> buildLog = settings.LoggingMode == LoggingMode.None ? _ => { } : log;
            if (ChartFileInputPlanBuilder.TryBuild(settings, buildLog, macroPlan, out IReadOnlyList<InputPlanEntry> runtimeInputPlan))
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

                if (settings.EnableCameraSafeMode)
                {
                    int cameraSafeCap = settings.CameraSafeStrictMode
                        ? 1
                        : Math.Max(1, settings.CameraSafeMaxHitsPerPlayerControlUpdate);
                    if (settings.MaxHitsPerPlayerControlUpdate > cameraSafeCap)
                    {
                        int previousMaxHits = settings.MaxHitsPerPlayerControlUpdate;
                        settings.MaxHitsPerPlayerControlUpdate = cameraSafeCap;
                        LogMinimal(log, $"Runtime input-pipeline plan active; camera-safe clamp MaxHitsPerPlayerControlUpdate from {previousMaxHits} to {cameraSafeCap}.");
                    }
                    else if (settings.MaxHitsPerPlayerControlUpdate < 1)
                    {
                        settings.MaxHitsPerPlayerControlUpdate = cameraSafeCap;
                    }
                }
                else if (settings.MaxHitsPerPlayerControlUpdate < 5000)
                {
                    int previousMaxHits = settings.MaxHitsPerPlayerControlUpdate;
                    settings.MaxHitsPerPlayerControlUpdate = 5000;
                    LogMinimal(log, $"Runtime input-pipeline plan active; raising MaxHitsPerPlayerControlUpdate from {previousMaxHits} to 5000 for dense directKeyTimes sections. EnableCameraSafeMode is OFF.");
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
            InternalMacroSettings? settings = SettingsField?.GetValue(__instance) as InternalMacroSettings;
            int keyCount = Math.Max(1, entry.EmittedHitCount);
            bool cameraSafeQueueOnly = settings != null &&
                                       settings.EnableCameraSafeMode &&
                                       settings.CameraSafeQueueOnlyMode;

            DirectKeyTimesTrace trace = new();
            trace.KeyViewerEnabled = settings != null && settings.EnableMacroKeyViewer;
            trace.RainEnabled = settings != null && settings.EnableMacroKeyViewer && settings.EnableKeyViewerRain;
            object? controller = ReflectionCache.GetSingletonInstance("scrController");
            bool asyncInputActive = ReflectionCache.TryReadBool("AsyncInputManager", out bool active, "isActive") && active;
            double prefixStartRealtime = Time.realtimeSinceStartupAsDouble;
            long prefixStartMemory = Profiler.GetTotalAllocatedMemoryLong();
            trace.MarkSetupDone();

            // v17 proved that faking ValidInputWasTriggered/CountValidKeysPressed is
            // unstable here: the synthetic hit window can be consumed by unrelated Up
            // events, and AsyncInput may skip the normal PlayerControl_Update body.
            // Inject directly at the game's real input queue instead:
            //   keyTimes.Add(now) x keyCount
            //   UpdateHoldKeys -> Hit(false)
            // HitInputEvent(false, Down) is still accepted by InputPatches while the
            // synthetic hit window is open.
            //
            // v47 keeps gameplay input untouched and moves directKeyTimes diagnostics off the hot logging path.
            // v50 camera-safe queue-only mode avoids forced Simulated_PlayerControl_Update.
            // Per-hit diagnostics are stored as numeric samples and dumped only when the scheduler stops.
            bool plainSingle =
                keyCount == 1 &&
                entry.RawEntryCount == 1 &&
                !entry.ContainsMidspin &&
                !entry.IsCompressed;

            if (!cameraSafeQueueOnly &&
                TryBuildDueBurst(
                    __instance,
                    dueCount,
                    out int burstEntryCount,
                    out int burstKeyCount,
                    out int burstTargetSeqId))
            {
                double marker = trace.Begin();
                int burstAfterFloor = DirectKeyTimesInputInjector.InjectBurst(
                    controller,
                    burstKeyCount,
                    burstTargetSeqId,
                    forceSimulation: asyncInputActive,
                    maxSimulationSteps: Math.Min(MaxBurstKeys + 64, Math.Max(1, burstKeyCount) + 64),
                    log);
                trace.AddDirectCall(marker);

                marker = trace.Begin();
                bool burstReachedFirstTarget = burstAfterFloor >= entry.FirstSeqId;
                trace.AddFloorCheck(marker);

                if (burstReachedFirstTarget)
                {
                    ResetStuckPlainSingle(entry.FirstSeqId);
                    currentFloorAfter = burstAfterFloor;
                    marker = trace.Begin();
                    trace.KeyViewerPulseCount += PulseMacroKeyViewer(__instance, entry, burstKeyCount);
                    trace.AddKeyViewerNotify(marker);
                    marker = trace.Begin();
                    RecordDirectKeyTimesSummary(log, burstKeyCount, currentFloorBefore, burstAfterFloor, asyncInputActive);
                    trace.AddSummary(marker);
                    trace.Finish();
                    LogDirectKeyTimesSpikeIfNeeded(__instance, log, "burst", prefixStartRealtime, prefixStartMemory, keyCount, burstKeyCount, dueCount, currentFloorBefore, burstAfterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive, trace);
                    __result = true;
                    return false;
                }

                trace.Finish();
            }

            double directMarker = trace.Begin();
            int afterFloor = DirectKeyTimesInputInjector.Inject(
                controller,
                keyCount,
                forceSimulation: asyncInputActive && !cameraSafeQueueOnly,
                allowDirectHitFallback: false,
                log);
            trace.AddDirectCall(directMarker);

            double floorMarker = trace.Begin();
            bool reachedTarget = afterFloor >= entry.FirstSeqId;
            trace.AddFloorCheck(floorMarker);

            if (reachedTarget)
            {
                ResetStuckPlainSingle(entry.FirstSeqId);
                currentFloorAfter = afterFloor;
                double keyViewerMarker = trace.Begin();
                trace.KeyViewerPulseCount += PulseMacroKeyViewer(__instance, entry, keyCount);
                trace.AddKeyViewerNotify(keyViewerMarker);
                double summaryMarker = trace.Begin();
                RecordDirectKeyTimesSummary(log, keyCount, currentFloorBefore, afterFloor, asyncInputActive);
                trace.AddSummary(summaryMarker);
                trace.Finish();
                LogDirectKeyTimesSpikeIfNeeded(__instance, log, "directKeyTimes", prefixStartRealtime, prefixStartMemory, keyCount, keyCount, dueCount, currentFloorBefore, afterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive, trace);
                __result = true;
                return false;
            }

            if (cameraSafeQueueOnly)
            {
                if (settings != null && settings.CameraSafePulseKeyViewerOnQueue && lastQueueOnlyKeyViewerPulseSeqId != entry.FirstSeqId)
                {
                    double keyViewerMarker = trace.Begin();
                    trace.KeyViewerPulseCount += PulseMacroKeyViewer(__instance, entry, keyCount);
                    trace.AddKeyViewerNotify(keyViewerMarker);
                    lastQueueOnlyKeyViewerPulseSeqId = entry.FirstSeqId;
                }

                // v52: in queue-only mode the floor usually advances in the game's
                // next normal update, so the reachedTarget branch is skipped. Still
                // update the lightweight summary counters so the MacroKeyViewer
                // pulse budget resets periodically instead of dying after 64 pulses.
                double queueSummaryMarker = trace.Begin();
                RecordDirectKeyTimesSummary(log, keyCount, currentFloorBefore, afterFloor, asyncInputActive);
                trace.AddSummary(queueSummaryMarker);

                currentFloorAfter = currentFloorBefore;
                trace.Finish();
                LogDirectKeyTimesSpikeIfNeeded(__instance, log, "queuedWait", prefixStartRealtime, prefixStartMemory, keyCount, keyCount, dueCount, currentFloorBefore, afterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive, trace);
                __result = false;
                return false;
            }

            if (plainSingle)
            {
                int attempts = RegisterStuckPlainSingle(entry.FirstSeqId);
                double lateMs = Math.Max(0.0, (clockSeconds - entry.FirstTargetTimeSeconds) * 1000.0);
                if (attempts >= 3 || lateMs >= 60.0)
                {
                    double fallbackMarker = trace.Begin();
                    int fallbackAfterFloor = DirectKeyTimesInputInjector.InvokeDirectHitOnly(
                        controller,
                        syntheticHitBudget: 2,
                        log);
                    trace.AddDirectCall(fallbackMarker);

                    double fallbackFloorMarker = trace.Begin();
                    bool fallbackReachedTarget = fallbackAfterFloor >= entry.FirstSeqId;
                    trace.AddFloorCheck(fallbackFloorMarker);

                    if (fallbackReachedTarget)
                    {
                        ResetStuckPlainSingle(entry.FirstSeqId);
                        currentFloorAfter = fallbackAfterFloor;
                        double keyViewerMarker = trace.Begin();
                        trace.KeyViewerPulseCount += PulseMacroKeyViewer(__instance, entry, keyCount);
                        trace.AddKeyViewerNotify(keyViewerMarker);
                        double summaryMarker = trace.Begin();
                        RecordDirectKeyTimesSummary(log, keyCount, currentFloorBefore, fallbackAfterFloor, asyncInputActive);
                        trace.AddSummary(summaryMarker);
                        trace.Finish();
                        LogDirectKeyTimesSpikeIfNeeded(__instance, log, "plainSingleDirectHitFallback", prefixStartRealtime, prefixStartMemory, keyCount, keyCount, dueCount, currentFloorBefore, fallbackAfterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive, trace);
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
            trace.Finish();
            LogDirectKeyTimesSpikeIfNeeded(__instance, log, "queuedNoAdvance", prefixStartRealtime, prefixStartMemory, keyCount, keyCount, dueCount, currentFloorBefore, afterFloor, entry.FirstSeqId, entry.LastSeqId, entry.FirstTargetTimeSeconds, clockSeconds, asyncInputActive, trace);
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

        private static void ResetMacroKeyViewerPulseBudgetIfWindowElapsed()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (lastDirectKeyTimesSummaryRealtime <= 0.0)
            {
                lastDirectKeyTimesSummaryRealtime = now;
                return;
            }

            if (now - lastDirectKeyTimesSummaryRealtime < 0.5)
            {
                return;
            }

            directKeyTimesEntriesSinceSummary = 0;
            directKeyTimesKeysSinceSummary = 0;
            directKeyTimesFloorAdvanceSinceSummary = 0;
            macroKeyViewerPulsesSinceSummary = 0;
            lastDirectKeyTimesSummaryRealtime = now;
        }

        private static int PulseMacroKeyViewer(InternalMacroService service, InputPlanEntry entry, int keyCount)
        {
            ResetMacroKeyViewerPulseBudgetIfWindowElapsed();

            InternalMacroSettings? settings = SettingsField?.GetValue(service) as InternalMacroSettings;
            if (settings == null || !settings.EnableMacroKeyViewer || keyCount <= 0)
            {
                return 0;
            }

            IReadOnlyList<string> configuredKeys = service.MacroKeyViewer.ConfigureKeys(settings.MacroKeyViewerKeysText);
            if (configuredKeys.Count == 0 && entry.AssignedKeyNames.Count == 0)
            {
                return 0;
            }

            // v36: the game input path still only queues keyTimes. Natural fingering is
            // a MacroKeyViewer layer: pulse the beat-bank assigned key names instead of
            // the old round-robin counter. Keep v29's UI load cap so dense sections do
            // not destabilize playback.
            if (keyCount >= 128)
            {
                return 0;
            }

            const int maxPulsesPerSummaryWindow = 64;
            if (macroKeyViewerPulsesSinceSummary >= maxPulsesPerSummaryWindow)
            {
                return 0;
            }

            double durationSeconds = Math.Max(0, settings.MacroKeyViewerPulseMs) / 1000.0;
            int remainingPulseBudget = maxPulsesPerSummaryWindow - macroKeyViewerPulsesSinceSummary;
            int pulseCount = Math.Min(Math.Min(keyCount, 32), remainingPulseBudget);
            int pulsed = 0;
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
                    pulsed++;
                }
                catch
                {
                    break;
                }
            }

            return pulsed;
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

            // v47/v52: keep the 0.5s accounting window because MacroKeyViewer
            // pulse throttling depends on it, but do not emit a string from the input
            // hot path. v52 also lets PulseMacroKeyViewer reset this window directly
            // so camera-safe queue-only mode cannot run out of pulse budget forever.
            ResetMacroKeyViewerPulseBudgetIfWindowElapsed();
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
            bool asyncInputActive,
            DirectKeyTimesTrace trace)
        {
            NaturalFingeringOptions.Load();
            double now = Time.realtimeSinceStartupAsDouble;
            double processingMs = (now - startRealtime) * 1000.0;
            double lateMs = Math.Max(0.0, (clockSeconds - targetTimeSeconds) * 1000.0);
            bool processingSpike = NaturalFingeringOptions.EnableLagSpikeLog &&
                                   processingMs >= Math.Max(0.1, NaturalFingeringOptions.LagSpikeLogMs);
            bool lateSpike = NaturalFingeringOptions.EnableLateSpikeLog &&
                             lateMs >= Math.Max(0.1, NaturalFingeringOptions.LateSpikeLogMs);
            string reason = processingSpike && lateSpike
                ? "late+processing"
                : lateSpike
                    ? "late"
                    : processingSpike
                        ? "processing"
                        : "sample";

            long memoryDeltaBytes = Profiler.GetTotalAllocatedMemoryLong() - startMemoryBytes;
            RecordDeferredTrace(
                operation,
                reason,
                clockSeconds,
                targetTimeSeconds,
                lateMs,
                processingMs,
                entryKeyCount,
                processedKeyCount,
                dueCount,
                beforeFloor,
                afterFloor,
                firstSeqId,
                lastSeqId,
                asyncInputActive,
                trace,
                memoryDeltaBytes / 1024.0,
                lateSpike,
                processingSpike);
        }

        private static void RecordDeferredTrace(
            string operation,
            string reason,
            double clockSeconds,
            double targetTimeSeconds,
            double lateMs,
            double processingMs,
            int entryKeyCount,
            int processedKeyCount,
            int dueCount,
            int beforeFloor,
            int afterFloor,
            int firstSeqId,
            int lastSeqId,
            bool asyncInputActive,
            DirectKeyTimesTrace trace,
            double memoryDeltaKb,
            bool lateSpike,
            bool processingSpike)
        {
            DeferredDirectKeyTimesTrace? existingEntry = DeferredTraceRing[deferredTraceWriteIndex];
            if (existingEntry == null)
            {
                existingEntry = new DeferredDirectKeyTimesTrace();
                DeferredTraceRing[deferredTraceWriteIndex] = existingEntry;
            }

            DeferredDirectKeyTimesTrace entry = existingEntry;
            entry.Operation = operation;
            entry.Reason = reason;
            entry.ClockSeconds = clockSeconds;
            entry.TargetTimeSeconds = targetTimeSeconds;
            entry.LateMs = lateMs;
            entry.ProcessingMs = processingMs;
            entry.EntryKeyCount = entryKeyCount;
            entry.ProcessedKeyCount = processedKeyCount;
            entry.DueCount = dueCount;
            entry.BeforeFloor = beforeFloor;
            entry.AfterFloor = afterFloor;
            entry.FirstSeqId = firstSeqId;
            entry.LastSeqId = lastSeqId;
            entry.AsyncInputActive = asyncInputActive;
            entry.StartFrame = trace.StartFrame;
            entry.EndFrame = trace.EndFrame;
            entry.StartDeltaTimeMs = trace.StartDeltaTimeMs;
            entry.EndDeltaTimeMs = trace.EndDeltaTimeMs;
            entry.StartUnscaledDeltaTimeMs = trace.StartUnscaledDeltaTimeMs;
            entry.EndUnscaledDeltaTimeMs = trace.EndUnscaledDeltaTimeMs;
            entry.SetupMs = trace.SetupMs;
            entry.DirectCallMs = trace.DirectCallMs;
            entry.FloorCheckMs = trace.FloorCheckMs;
            entry.KeyViewerNotifyMs = trace.KeyViewerNotifyMs;
            entry.SummaryMs = trace.SummaryMs;
            entry.TotalMs = trace.TotalMs;
            entry.KeyViewerEnabled = trace.KeyViewerEnabled;
            entry.RainEnabled = trace.RainEnabled;
            entry.KeyViewerPulseCount = trace.KeyViewerPulseCount;
            entry.MacroKeyViewerPulsesWindow = macroKeyViewerPulsesSinceSummary;
            entry.MemoryDeltaKb = memoryDeltaKb;

            deferredTraceWriteIndex = (deferredTraceWriteIndex + 1) % DeferredTraceCapacity;
            if (deferredTraceCount < DeferredTraceCapacity)
            {
                deferredTraceCount++;
            }

            deferredTraceTotalCount++;
            if (lateSpike)
            {
                deferredLateSpikeCount++;
            }

            if (processingSpike)
            {
                deferredProcessingSpikeCount++;
            }

            if (operation == "queuedNoAdvance")
            {
                deferredQueuedNoAdvanceCount++;
            }

            deferredMaxDueCount = Math.Max(deferredMaxDueCount, dueCount);
            deferredMaxLateMs = Math.Max(deferredMaxLateMs, lateMs);
            deferredMaxProcessingMs = Math.Max(deferredMaxProcessingMs, processingMs);
            deferredMaxDeltaMs = Math.Max(deferredMaxDeltaMs, Math.Max(trace.StartDeltaTimeMs, trace.EndDeltaTimeMs));
            deferredMaxAfterFloor = Math.Max(deferredMaxAfterFloor, afterFloor);
        }

        internal static void ResetDeferredDiagnostics()
        {
            for (int i = 0; i < DeferredTraceRing.Length; i++)
            {
                if (DeferredTraceRing[i] == null)
                {
                    DeferredTraceRing[i] = new DeferredDirectKeyTimesTrace();
                }
            }

            deferredTraceWriteIndex = 0;
            deferredTraceCount = 0;
            deferredTraceTotalCount = 0;
            deferredLateSpikeCount = 0;
            deferredProcessingSpikeCount = 0;
            deferredQueuedNoAdvanceCount = 0;
            deferredMaxDueCount = 0;
            deferredMaxLateMs = 0.0;
            deferredMaxProcessingMs = 0.0;
            deferredMaxDeltaMs = 0.0;
            deferredMaxAfterFloor = 0;
            directKeyTimesEntriesSinceSummary = 0;
            directKeyTimesKeysSinceSummary = 0;
            directKeyTimesFloorAdvanceSinceSummary = 0;
            macroKeyViewerPulsesSinceSummary = 0;
            lastDirectKeyTimesSummaryRealtime = 0.0;
            ResetStuckPlainSingle(-1);
            lastQueueOnlyKeyViewerPulseSeqId = -1;
        }

        internal static void DumpRecentDirectKeyTimes(Action<string>? log, string stopReason, InternalMacroSettings? settings)
        {
            if (log == null || deferredTraceCount <= 0 || !NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Minimal))
            {
                return;
            }

            bool isWin = IsWinStopReason(stopReason);
            bool isNormalStop = isWin || IsNormalStopReason(stopReason);
            bool dumpOnWin = settings?.DirectKeyTimesDumpOnWin ?? false;
            bool dumpOnlyOnFailure = settings?.DirectKeyTimesDumpOnlyOnFailure ?? true;
            if ((isWin && !dumpOnWin) || (dumpOnlyOnFailure && isNormalStop))
            {
                return;
            }

            int requestedDumpCount = settings?.DirectKeyTimesDeferredDumpEntries ?? 32;
            int dumpCount = Math.Max(0, Math.Min(Math.Min(requestedDumpCount, DeferredTraceCapacity), deferredTraceCount));
            if (dumpCount <= 0)
            {
                return;
            }

            log($"directKeyTimes deferred diagnostics v50. stopReason={stopReason} stored={deferredTraceCount}/{DeferredTraceCapacity} dumping={dumpCount}/{deferredTraceCount} totalSamples={deferredTraceTotalCount} lateSpikes={deferredLateSpikeCount} processingSpikes={deferredProcessingSpikeCount} queuedNoAdvance={deferredQueuedNoAdvanceCount} maxLateMs={deferredMaxLateMs:F3} maxProcessingMs={deferredMaxProcessingMs:F3} maxDeltaMs={deferredMaxDeltaMs:F2} maxDueCount={deferredMaxDueCount} maxAfterFloor={deferredMaxAfterFloor}");

            int start = (deferredTraceWriteIndex - dumpCount + DeferredTraceCapacity) % DeferredTraceCapacity;
            for (int i = 0; i < dumpCount; i++)
            {
                int ringIndex = (start + i) % DeferredTraceCapacity;
                DeferredDirectKeyTimesTrace? entry = DeferredTraceRing[ringIndex];
                if (entry == null)
                {
                    continue;
                }

                log(
                    $"directKeyTimes deferred[{i + 1}/{dumpCount}]. reason={entry.Reason} op={entry.Operation} lateMs={entry.LateMs:F3} processingMs={entry.ProcessingMs:F3} clock={entry.ClockSeconds:F6}s target={entry.TargetTimeSeconds:F6}s entryKeys={entry.EntryKeyCount} processedKeys={entry.ProcessedKeyCount} dueCount={entry.DueCount} floor={entry.BeforeFloor}->{entry.AfterFloor} seqID={entry.FirstSeqId}-{entry.LastSeqId} asyncInputActive={entry.AsyncInputActive} frame={entry.StartFrame}->{entry.EndFrame} deltaMs={entry.StartDeltaTimeMs:F2}->{entry.EndDeltaTimeMs:F2} unscaledDeltaMs={entry.StartUnscaledDeltaTimeMs:F2}->{entry.EndUnscaledDeltaTimeMs:F2} splitMs=setup:{entry.SetupMs:F3},direct:{entry.DirectCallMs:F3},floor:{entry.FloorCheckMs:F3},kv:{entry.KeyViewerNotifyMs:F3},summary:{entry.SummaryMs:F3},total:{entry.TotalMs:F3} keyViewerEnabled={entry.KeyViewerEnabled} rainEnabled={entry.RainEnabled} kvPulsesThisEntry={entry.KeyViewerPulseCount} macroKeyViewerPulsesWindow={entry.MacroKeyViewerPulsesWindow} memDeltaKB={entry.MemoryDeltaKb:F1}");
            }
        }

        private static bool IsWinStopReason(string stopReason)
        {
            return stopReason.IndexOf("Won_Update", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   stopReason.IndexOf("won", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsNormalStopReason(string stopReason)
        {
            return stopReason.IndexOf("end of plan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   stopReason.IndexOf("settings disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   stopReason.IndexOf("mod unloaded", StringComparison.OrdinalIgnoreCase) >= 0;
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
