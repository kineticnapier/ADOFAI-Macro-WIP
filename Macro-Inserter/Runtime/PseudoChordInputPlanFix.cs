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
        private static readonly Harmony Harmony = new Harmony("Macro-Inserter.PseudoChordInputPlanFix.v13");
        private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
        private static readonly FieldInfo? LogField = AccessTools.Field(typeof(InternalMacroService), "log");

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
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v13 installed by {reason}. buildPatched={buildPatched} firePatched={firePatched}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v13 install failed: {ex}");
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
                // TryFirePseudoChordGroup(). The v11/v12 prefix on that method
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
                    log($"Runtime input-pipeline plan active; forcing FireMode DirectHit branch. selectedFireMode={settings.FireMode} actualInjection=InputPatchState");
                    settings.FireMode = FireMode.DirectHit;
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

            // This runs from the PlayerControl_Update prefix. scrController then executes
            // its normal input pipeline during the original PlayerControl_Update body:
            //   HitAutoFloors -> CountValidKeysPressed -> keyTimes.Add(...)
            //   UpdateHoldKeys -> Hit(false)
            //
            // Important: do NOT report success to the original scheduler here.
            // At prefix time the game has not consumed the virtual input yet, so advancing
            // nextIndex immediately can desync when the input is missed or consumed later in
            // the same Unity frame. Instead we schedule the frame input, return false, and
            // let the next PlayerControl_Update confirm progress through the existing
            // floorGuard path (currentFloor >= FirstSeqId). If the floor did not move, the
            // same entry is retried until MaxLateRetryMs/Fault handling stops it.
            InputPatchState.BeginFrame(keyCount);
            currentFloorAfter = currentFloorBefore;
            log?.Invoke(
                $"runtimeInputPatch queued; waiting for floor confirmation. keyCount={keyCount} seqID={entry.FirstSeqId}-{entry.LastSeqId} targetTime={entry.FirstTargetTimeSeconds:F6}s spanMs={entry.SpanMs:F3} rawEntryCount={entry.RawEntryCount} containsMidspin={entry.ContainsMidspin} currentFloorBefore={currentFloorBefore} expectedAfter={entry.LastSeqId} dueCount={dueCount}");
            __result = false;
            return false;
        }
    }
}
