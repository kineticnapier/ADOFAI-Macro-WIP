using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private static readonly Harmony Harmony = new Harmony("Macro-Inserter.PseudoChordInputPlanFix.v23");
        private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
        private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");
        private static readonly MethodInfo? PulseMacroKeyViewerMethod = AccessTools.Method(typeof(InternalMacroService), "PulseMacroKeyViewer");

        private static int directKeyTimesEntriesSinceSummary;
        private static int directKeyTimesKeysSinceSummary;
        private static int directKeyTimesFloorAdvanceSinceSummary;
        private static int macroKeyViewerPulsesSinceSummary;
        private static double lastDirectKeyTimesSummaryRealtime;
        private static int stuckPlainSingleSeqId = -1;
        private static int stuckPlainSingleAttemptCount;

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
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v23 installed by {reason}. buildPatched={buildPatched} firePatched={firePatched}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v23 install failed: {ex}");
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
                // TryFirePseudoChordGroup(). The v11/v12/v13/v14/v15/v17/v18/v20/v21/v23 prefix on that method
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
                    log($"Runtime input-pipeline plan active; forcing FireMode DirectHit branch. selectedFireMode={settings.FireMode} actualInjection=DirectKeyTimes");
                    settings.FireMode = FireMode.DirectHit;
                }

                if (!settings.EnableHighDensityMode)
                {
                    settings.EnableHighDensityMode = true;
                    log("Runtime input-pipeline plan active; forcing EnableHighDensityMode=True so DirectKeyTimes can keep up with dense input sections.");
                }

                if (settings.MaxHitsPerPlayerControlUpdate < 64)
                {
                    int previousMaxHits = settings.MaxHitsPerPlayerControlUpdate;
                    settings.MaxHitsPerPlayerControlUpdate = 64;
                    log($"Runtime input-pipeline plan active; raising MaxHitsPerPlayerControlUpdate from {previousMaxHits} to 64.");
                }

                __result = runtimeInputPlan;
                return false;
            }

            log("Runtime input-pipeline plan unavailable; disabling internal input plan instead of falling back to DirectHit.");
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
            // keyTimes/update path. v23 keeps directKeyTimes as the primary path for
            // every entry, and only uses DirectHit as a delayed emergency retry for a
            // plain single that is already stuck.
            bool plainSingle =
                keyCount == 1 &&
                entry.RawEntryCount == 1 &&
                !entry.ContainsMidspin &&
                !entry.IsCompressed;

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
                PulseMacroKeyViewer(__instance, keyCount);
                RecordDirectKeyTimesSummary(log, keyCount, currentFloorBefore, afterFloor, asyncInputActive);
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
                    log?.Invoke(
                        $"plainSingle delayed DirectHit fallback tried. seqID={entry.FirstSeqId} attempts={attempts} lateMs={lateMs:F3} currentFloorBefore={currentFloorBefore} afterFloor={fallbackAfterFloor}");

                    if (fallbackAfterFloor >= entry.FirstSeqId)
                    {
                        ResetStuckPlainSingle(entry.FirstSeqId);
                        currentFloorAfter = fallbackAfterFloor;
                        PulseMacroKeyViewer(__instance, keyCount);
                        RecordDirectKeyTimesSummary(log, keyCount, currentFloorBefore, fallbackAfterFloor, asyncInputActive);
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
            log?.Invoke(
                $"directKeyTimes queued; waiting for floor confirmation. keyCount={keyCount} seqID={entry.FirstSeqId}-{entry.LastSeqId} targetTime={entry.FirstTargetTimeSeconds:F6}s spanMs={entry.SpanMs:F3} rawEntryCount={entry.RawEntryCount} containsMidspin={entry.ContainsMidspin} isNearMidspin={entry.IsNearMidspin} plainSingle={plainSingle} stuckPlainSingleAttempts={stuckPlainSingleAttemptCount} currentFloorBefore={currentFloorBefore} currentFloorAfter={afterFloor} expectedAfter={entry.LastSeqId} dueCount={dueCount} asyncInputActive={asyncInputActive}");
            __result = false;
            return false;
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

        private static void PulseMacroKeyViewer(InternalMacroService service, int keyCount)
        {
            if (PulseMacroKeyViewerMethod == null || keyCount <= 0)
            {
                return;
            }

            int pulseCount = Math.Min(keyCount, 64);
            for (int i = 0; i < pulseCount; i++)
            {
                try
                {
                    PulseMacroKeyViewerMethod.Invoke(service, Array.Empty<object>());
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
            log?.Invoke(
                $"directKeyTimes summary. entries={directKeyTimesEntriesSinceSummary} keys={directKeyTimesKeysSinceSummary} floorAdvance={directKeyTimesFloorAdvanceSinceSummary} macroKeyViewerPulses={macroKeyViewerPulsesSinceSummary} elapsedMs={elapsed * 1000.0:F1} approxKps={keysPerSecond:F1} asyncInputActive={asyncInputActive}");

            directKeyTimesEntriesSinceSummary = 0;
            directKeyTimesKeysSinceSummary = 0;
            directKeyTimesFloorAdvanceSinceSummary = 0;
            macroKeyViewerPulsesSinceSummary = 0;
            lastDirectKeyTimesSummaryRealtime = now;
        }

    }
}
