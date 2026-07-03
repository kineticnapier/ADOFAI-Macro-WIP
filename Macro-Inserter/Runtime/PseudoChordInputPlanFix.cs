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
        private static readonly Harmony Harmony = new Harmony("Macro-Inserter.PseudoChordInputPlanFix.v10");
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
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v10 installed by {reason}. buildPatched={buildPatched} firePatched={firePatched}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v10 install failed: {ex}");
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

            if (ChartFileInputPlanBuilder.TryBuild(settings, log, macroPlan, out IReadOnlyList<InputPlanEntry> chartInputPlan))
            {
                __result = chartInputPlan;
                return false;
            }

            log("Chart file input plan unavailable; runtime floor fallback is disabled to avoid unsafe pseudoChord over-hit/under-hit behavior.");
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
            if (!entry.IsChartFileChord)
            {
                return true;
            }

            Action<string>? log = LogField?.GetValue(__instance) as Action<string>;
            int keyCount = Math.Max(1, entry.EmittedHitCount);
            InputPatchState.BeginFrame(keyCount);
            currentFloorAfter = entry.LastSeqId;
            log?.Invoke(
                $"chartChord scheduled input patch. keyCount={keyCount} seqID={entry.FirstSeqId}-{entry.LastSeqId} targetTime={entry.FirstTargetTimeSeconds:F6}s spanMs={entry.SpanMs:F3} currentFloorBefore={currentFloorBefore} expectedAfter={currentFloorAfter} dueCount={dueCount}");
            __result = true;
            return false;
        }
    }
}
