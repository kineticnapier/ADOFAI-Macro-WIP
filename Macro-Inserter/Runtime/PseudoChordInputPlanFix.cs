using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
        private static readonly Harmony Harmony = new Harmony("Macro-Inserter.PseudoChordInputPlanFix.v64");
        private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
        private static readonly FieldInfo? MainServiceField = AccessTools.Field(typeof(Main), "service");
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
        private static bool perfectOverrideSeenActive;
        private static bool hitErrorMeterResetForActiveRun;

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

                bool audioPatched = TryPatchOneShotAudio();
                bool visualPatched = TryPatchUltraDensityVisualSuppressors();
                bool perfectOverridePatched = TryPatchVerificationPerfectOverride();

                patched = buildPatched && firePatched;
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v64 installed by {reason}. buildPatched={buildPatched} firePatched={firePatched} audioPatched={audioPatched} visualPatched={visualPatched} perfectOverridePatched={perfectOverridePatched} rainOverlay=True uiPatchDisabled=True loadPatchDisabled=True logPatchDisabled=True");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] PseudoChordInputPlanFix v64 install failed: {ex}");
            }
        }



        private static bool TryPatchVerificationPerfectOverride()
        {
            int patchedCount = 0;
            try
            {
                MethodInfo? getHitMargin = AccessTools.Method(
                    typeof(scrMisc),
                    nameof(scrMisc.GetHitMargin),
                    new[] { typeof(float), typeof(float), typeof(bool), typeof(float), typeof(float), typeof(double) });
                MethodInfo? getHitMarginPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrMiscGetHitMarginPrefix));
                if (getHitMargin != null && getHitMarginPrefix != null)
                {
                    Harmony.Patch(getHitMargin, prefix: new HarmonyMethod(getHitMarginPrefix));
                    patchedCount++;
                }

                MethodInfo? addHit = AccessTools.Method(typeof(scrMistakesManager), nameof(scrMistakesManager.AddHit), new[] { typeof(HitMargin) });
                MethodInfo? addHitPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrMistakesManagerAddHitPrefix));
                if (addHit != null && addHitPrefix != null)
                {
                    Harmony.Patch(addHit, prefix: new HarmonyMethod(addHitPrefix));
                    patchedCount++;
                }

                MethodInfo? showHitText = AccessTools.Method(typeof(scrController), nameof(scrController.ShowHitText), new[] { typeof(HitMargin), typeof(Vector3), typeof(float) });
                MethodInfo? showHitTextPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrControllerShowHitTextPrefix));
                if (showHitText != null && showHitTextPrefix != null)
                {
                    Harmony.Patch(showHitText, prefix: new HarmonyMethod(showHitTextPrefix));
                    patchedCount++;
                }

                MethodInfo? onDamage = AccessTools.Method(typeof(scrController), nameof(scrController.OnDamage), new[] { typeof(bool), typeof(bool), typeof(bool), typeof(HitMargin) });
                MethodInfo? onDamagePrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrControllerOnDamagePrefix));
                if (onDamage != null && onDamagePrefix != null)
                {
                    Harmony.Patch(onDamage, prefix: new HarmonyMethod(onDamagePrefix));
                    patchedCount++;
                }

                MethodInfo? failAction = AccessTools.Method(typeof(scrController), nameof(scrController.FailAction), new[] { typeof(bool), typeof(bool), typeof(string), typeof(bool) });
                MethodInfo? failActionPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrControllerFailActionPrefix));
                if (failAction != null && failActionPrefix != null)
                {
                    Harmony.Patch(failAction, prefix: new HarmonyMethod(failActionPrefix));
                    patchedCount++;
                }

                MethodInfo? switchChosen = AccessTools.Method(typeof(scrPlanet), nameof(scrPlanet.SwitchChosen));
                MethodInfo? switchChosenPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrPlanetSwitchChosenPrefix));
                MethodInfo? switchChosenPostfix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrPlanetSwitchChosenPostfix));
                if (switchChosen != null && switchChosenPrefix != null && switchChosenPostfix != null)
                {
                    Harmony.Patch(switchChosen, prefix: new HarmonyMethod(switchChosenPrefix), postfix: new HarmonyMethod(switchChosenPostfix));
                    patchedCount++;
                }

                MethodInfo? errorMeterAddHit = AccessTools.Method(typeof(scrHitErrorMeter), nameof(scrHitErrorMeter.AddHit), new[] { typeof(float), typeof(float) });
                MethodInfo? errorMeterAddHitPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrHitErrorMeterAddHitPrefix));
                if (errorMeterAddHit != null && errorMeterAddHitPrefix != null)
                {
                    Harmony.Patch(errorMeterAddHit, prefix: new HarmonyMethod(errorMeterAddHitPrefix));
                    patchedCount++;
                }

                MethodInfo? mistakesGetHits = AccessTools.Method(typeof(scrMistakesManager), nameof(scrMistakesManager.GetHits), new[] { typeof(HitMargin) });
                MethodInfo? mistakesGetHitsPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrMistakesManagerGetHitsPrefix));
                if (mistakesGetHits != null && mistakesGetHitsPrefix != null)
                {
                    Harmony.Patch(mistakesGetHits, prefix: new HarmonyMethod(mistakesGetHitsPrefix));
                    patchedCount++;
                }

                MethodInfo? controllerGetHits = AccessTools.Method(typeof(scrController), "GetHits", new[] { typeof(HitMargin) });
                MethodInfo? controllerGetHitsPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrControllerGetHitsPrefix));
                if (controllerGetHits != null && controllerGetHitsPrefix != null)
                {
                    Harmony.Patch(controllerGetHits, prefix: new HarmonyMethod(controllerGetHitsPrefix));
                    patchedCount++;
                }

                MethodInfo? onLandOnPortal = AccessTools.Method(typeof(scrController), nameof(scrController.OnLandOnPortal), new[] { typeof(Portal), typeof(string) });
                MethodInfo? onLandOnPortalPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(ScrControllerOnLandOnPortalPrefix));
                if (onLandOnPortal != null && onLandOnPortalPrefix != null)
                {
                    Harmony.Patch(onLandOnPortal, prefix: new HarmonyMethod(onLandOnPortalPrefix));
                    patchedCount++;
                }

                Debug.Log($"[Macro-Inserter] Verification PerfectOverride v64 patched. methods={patchedCount}");
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] Verification PerfectOverride v64 patch failed: {ex.GetType().Name}: {ex.Message}");
            }

            return patchedCount > 0;
        }

        private sealed class SwitchChosenPerfectOverrideState
        {
            public bool Active;
            public scrFloor? LandingFloor;
            public bool OldMultipressPenalty;
            public bool OldMultipressAndHasPressedFirstPress;
            public int OldKeyLimiterOverCounter;
        }

        private static bool ScrHitErrorMeterAddHitPrefix(scrHitErrorMeter __instance, ref float angleDiff, ref float marginScale)
        {
            if (!ShouldForcePerfectOverride())
            {
                return true;
            }

            // The bottom hit-error meter is fed directly from raw angle error before the normal
            // HitMargin is overridden. Clear any old non-perfect ticks once, then suppress new
            // meter ticks entirely while verification PerfectOverride is active. This removes
            // LPerfect/Late/TooLate remnants and avoids thousands of UI tweens in ultra KPS maps.
            if (!hitErrorMeterResetForActiveRun && __instance != null)
            {
                try
                {
                    __instance.Reset();
                }
                catch
                {
                    // Keep gameplay running even if the UI object is mid-destroy.
                }

                hitErrorMeterResetForActiveRun = true;
            }

            angleDiff = 0f;
            marginScale = 1f;
            return false;
        }

        private static bool ScrMistakesManagerGetHitsPrefix(HitMargin hit, ref int __result)
        {
            if (!ShouldForcePerfectOverrideForResultUi())
            {
                return true;
            }

            // v64: Do not sanitize the whole hit/floor history from a UI getter.
            // GetHits can be called several times per frame, and scanning thousands of
            // hitMargins/floors here makes dense charts crawl. Keep it O(1).
            __result = GetSanitizedHitCount(hit);
            return false;
        }

        private static bool ScrControllerGetHitsPrefix(HitMargin hitMargin, ref int __result)
        {
            if (!ShouldForcePerfectOverrideForResultUi())
            {
                return true;
            }

            __result = GetSanitizedHitCount(hitMargin);
            return false;
        }

        private static void ScrControllerOnLandOnPortalPrefix()
        {
            // v64: This used to call SanitizePerfectOverrideHitData() on every landing.
            // On 10k+ tile charts that becomes O(n^2) and causes severe lag.
            // Per-hit conversion is already handled by AddHit/GetHitMargin/ShowHitText,
            // and the bottom error meter is handled by scrHitErrorMeter.AddHit.
        }

        private static bool ScrMiscGetHitMarginPrefix(ref HitMargin __result)
        {
            if (!ShouldForcePerfectOverride())
            {
                return true;
            }

            __result = HitMargin.Perfect;
            return false;
        }

        private static void ScrMistakesManagerAddHitPrefix(ref HitMargin hit)
        {
            if (!ShouldForcePerfectOverride())
            {
                return;
            }

            if (hit != HitMargin.Perfect && hit != HitMargin.Auto)
            {
                hit = HitMargin.Perfect;
            }
        }

        private static void ScrControllerShowHitTextPrefix(ref HitMargin hitMargin)
        {
            if (!ShouldForcePerfectOverride())
            {
                return;
            }

            if (hitMargin != HitMargin.Perfect && hitMargin != HitMargin.Auto)
            {
                hitMargin = HitMargin.Perfect;
            }
        }

        private static bool ScrControllerOnDamagePrefix(ref bool __result)
        {
            if (!ShouldForcePerfectOverride())
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool ScrControllerFailActionPrefix()
        {
            return !ShouldForcePerfectOverride();
        }

        private static void ScrPlanetSwitchChosenPrefix(scrPlanet __instance, ref SwitchChosenPerfectOverrideState? __state)
        {
            __state = null;
            if (!ShouldForcePerfectOverride() || __instance == null || __instance.controller == null)
            {
                return;
            }

            __state = new SwitchChosenPerfectOverrideState
            {
                Active = true,
                LandingFloor = __instance.currfloor == null ? null : __instance.currfloor.nextfloor,
                OldMultipressPenalty = __instance.controller.multipressPenalty,
                OldMultipressAndHasPressedFirstPress = __instance.controller.multipressAndHasPressedFirstPress,
                OldKeyLimiterOverCounter = __instance.controller.keyLimiterOverCounter,
            };

            // High-density verification can trip the game's multipress/key-limiter guards even when
            // the scheduled input stream is intentional. Do not let those guards convert a valid
            // macro input into OverPress/Fail while the internal verifier is actively running.
            __instance.controller.multipressPenalty = false;
            __instance.controller.multipressAndHasPressedFirstPress = false;
            __instance.controller.keyLimiterOverCounter = 0;
        }

        private static void ScrPlanetSwitchChosenPostfix(scrPlanet __instance, SwitchChosenPerfectOverrideState? __state)
        {
            if (__state == null || !__state.Active || __instance == null || __instance.controller == null)
            {
                return;
            }

            __instance.controller.multipressPenalty = __state.OldMultipressPenalty;
            __instance.controller.multipressAndHasPressedFirstPress = __state.OldMultipressAndHasPressedFirstPress;
            __instance.controller.keyLimiterOverCounter = Math.Min(__instance.controller.keyLimiterOverCounter, __state.OldKeyLimiterOverCounter);

            if (__state.LandingFloor != null)
            {
                __state.LandingFloor.grade = HitMargin.Perfect;
            }
        }

        private static bool ShouldForcePerfectOverride()
        {
            InternalMacroService? service = MainServiceField?.GetValue(null) as InternalMacroService;
            bool active = service != null && service.IsInternalMacroActive;
            if (active)
            {
                perfectOverrideSeenActive = true;
            }

            return active;
        }

        private static bool ShouldForcePerfectOverrideForResultUi()
        {
            InternalMacroService? service = MainServiceField?.GetValue(null) as InternalMacroService;
            bool active = service != null && service.IsInternalMacroActive;
            if (active)
            {
                perfectOverrideSeenActive = true;
            }

            return active || perfectOverrideSeenActive;
        }

        private static void SanitizePerfectOverrideHitData()
        {
            // v64: Kept only as a cheap final cleanup helper. Do not scan floor history here.
            // The old v62 implementation walked hitMargins and all floors from 0..currentSeqID,
            // which made dense charts extremely slow when called from UI getters / landing hooks.
            try
            {
                int[] counts = scrMistakesManager.hitMarginsCount;
                int perfectCount = 0;
                int autoCount = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (i == (int)HitMargin.Perfect)
                    {
                        perfectCount += counts[i];
                    }
                    else if (i == (int)HitMargin.Auto)
                    {
                        autoCount += counts[i];
                    }
                    else
                    {
                        perfectCount += counts[i];
                        counts[i] = 0;
                    }
                }

                if ((int)HitMargin.Perfect >= 0 && (int)HitMargin.Perfect < counts.Length)
                {
                    counts[(int)HitMargin.Perfect] = perfectCount;
                }

                if ((int)HitMargin.Auto >= 0 && (int)HitMargin.Auto < counts.Length)
                {
                    counts[(int)HitMargin.Auto] = autoCount;
                }
            }
            catch
            {
                // Display/stat cleanup only. Never interrupt verification playback.
            }
        }

        private static int GetSanitizedHitCount(HitMargin hit)
        {
            int[] counts = scrMistakesManager.hitMarginsCount;
            if (hit == HitMargin.Auto)
            {
                return ((int)HitMargin.Auto >= 0 && (int)HitMargin.Auto < counts.Length) ? counts[(int)HitMargin.Auto] : 0;
            }

            if (hit == HitMargin.Perfect)
            {
                int total = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    if (i == (int)HitMargin.Auto)
                    {
                        continue;
                    }

                    total += counts[i];
                }

                return total;
            }

            return 0;
        }

        private static bool TryPatchUltraDensityVisualSuppressors()
        {
            // v60: source-informed ultra-density survival.
            // Do NOT kill DOTween active tween/sequence registration; that black-screens playback.
            // Instead:
            //   1) clamp DOTween.SetTweensCapacity so ADOStartup's 500/50 reset cannot undo our reserve,
            //   2) transpile known ADOFAI call sites that call Resources.UnloadUnusedAssets(),
            //      replacing them with a guarded wrapper.
            bool capacityPatch = TryPatchDOTweenSetTweensCapacity();
            bool unloadPatch = TryPatchKnownUnloadUnusedAssetsCallSites();
            TryPreallocateDOTweenCapacity();
            Debug.Log($"[Macro-Inserter] Ultra-density v60 patches: dotweenCapacityPatch={capacityPatch} unloadCallsitePatch={unloadPatch} dotweenKillPatch=False");
            return capacityPatch || unloadPatch;
        }

        private static bool TryPatchDOTweenSetTweensCapacity()
        {
            try
            {
                MethodInfo? prefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(DOTweenSetTweensCapacityPrefix));
                if (prefix == null)
                {
                    return false;
                }

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type? dotweenType = assembly.GetType("DG.Tweening.DOTween");
                    if (dotweenType == null)
                    {
                        continue;
                    }

                    MethodInfo? setTweensCapacity = AccessTools.Method(dotweenType, "SetTweensCapacity", new[] { typeof(int), typeof(int) });
                    if (setTweensCapacity == null)
                    {
                        continue;
                    }

                    Harmony.Patch(setTweensCapacity, prefix: new HarmonyMethod(prefix));
                    Debug.Log("[Macro-Inserter] DOTween.SetTweensCapacity clamp patch applied for ultra-density macro.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] DOTween.SetTweensCapacity clamp patch failed: {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }

        private static bool TryPatchKnownUnloadUnusedAssetsCallSites()
        {
            try
            {
                MethodInfo? transpiler = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(UnloadUnusedAssetsCallSiteTranspiler));
                if (transpiler == null)
                {
                    return false;
                }

                int patchedCount = 0;
                patchedCount += PatchMethodsByName("scnGame", "Awake", transpiler);
                patchedCount += PatchMethodsByName("scnGame", "LoadLevel", transpiler);
                patchedCount += PatchMethodsByName("scnEditor", "SwitchToEditMode", transpiler);

                if (patchedCount > 0)
                {
                    Debug.Log($"[Macro-Inserter] Resources.UnloadUnusedAssets call-site guard patched. methods={patchedCount}");
                    return true;
                }

                Debug.Log("[Macro-Inserter] Resources.UnloadUnusedAssets call-site guard found no target methods.");
                return false;
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] Resources.UnloadUnusedAssets call-site guard patch failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static int PatchMethodsByName(string typeName, string methodName, MethodInfo transpiler)
        {
            Type? type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                return 0;
            }

            int count = 0;
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name != methodName)
                {
                    continue;
                }

                Harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                count++;
            }

            return count;
        }

        private static IEnumerable<CodeInstruction> UnloadUnusedAssetsCallSiteTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo? original = AccessTools.Method(typeof(Resources), nameof(Resources.UnloadUnusedAssets), Type.EmptyTypes);
            MethodInfo? replacement = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(GuardedUnloadUnusedAssets));
            foreach (CodeInstruction instruction in instructions)
            {
                if (original != null && replacement != null && instruction.Calls(original))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static AsyncOperation? GuardedUnloadUnusedAssets()
        {
            try
            {
                if (ShouldSuppressUnloadUnusedAssets())
                {
                    return null;
                }
            }
            catch
            {
                // Fall through to the game call.
            }

            return Resources.UnloadUnusedAssets();
        }

        private static void DOTweenSetTweensCapacityPrefix(
            [HarmonyArgument(0)] ref int tweenersCapacity,
            [HarmonyArgument(1)] ref int sequencesCapacity)
        {
            try
            {
                if (!ShouldForceHighDOTweenCapacity())
                {
                    return;
                }

                if (tweenersCapacity < 200000)
                {
                    tweenersCapacity = 200000;
                }

                if (sequencesCapacity < 20000)
                {
                    sequencesCapacity = 20000;
                }
            }
            catch
            {
                // Keep DOTween usable if anything goes wrong.
            }
        }

        private static int TryPatchDOTweenActiveTweenMethods()
        {
            int patchedCount = 0;
            MethodInfo? dotweenPrefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), nameof(DOTweenAddActiveTweenPrefix));
            if (dotweenPrefix == null)
            {
                return 0;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? tweenManagerType = assembly.GetType("DG.Tweening.Core.TweenManager")
                    ?? assembly.GetType("DG.Tweening.TweenManager");
                if (tweenManagerType == null)
                {
                    continue;
                }

                foreach (MethodInfo method in tweenManagerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    string name = method.Name;
                    if (name != "AddActiveTween" && name != "AddActiveSequence")
                    {
                        continue;
                    }

                    try
                    {
                        Harmony.Patch(method, prefix: new HarmonyMethod(dotweenPrefix));
                        patchedCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.Log($"[Macro-Inserter] DOTween visual suppressor patch skipped: {method.DeclaringType?.FullName}.{method.Name}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            if (patchedCount > 0)
            {
                Debug.Log($"[Macro-Inserter] DOTween visual suppressor patched. methods={patchedCount}");
            }
            else
            {
                Debug.Log("[Macro-Inserter] DOTween visual suppressor found no patchable TweenManager methods.");
            }

            return patchedCount;
        }

        private static void TryPreallocateDOTweenCapacity()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type? dotweenType = assembly.GetType("DG.Tweening.DOTween");
                    if (dotweenType == null)
                    {
                        continue;
                    }

                    MethodInfo? setTweensCapacity = AccessTools.Method(dotweenType, "SetTweensCapacity", new[] { typeof(int), typeof(int) });
                    if (setTweensCapacity == null)
                    {
                        continue;
                    }

                    // Avoid repeated 1250 -> 3125 -> 7812 -> ... automatic reallocations.
                    // This does not create tweens; it only reserves the arrays once.
                    setTweensCapacity.Invoke(null, new object[] { 200000, 20000 });
                    Debug.Log("[Macro-Inserter] DOTween capacity preallocated for ultra-density macro. tweens=200000 sequences=20000");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] DOTween capacity preallocation skipped: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool ResourcesUnloadUnusedAssetsPrefix(ref AsyncOperation __result)
        {
            // Kept only so older compiled references/settings remain harmless.
            // This method is intentionally no longer patched in v57.
            return true;
        }

        private static bool DOTweenAddActiveTweenPrefix()
        {
            try
            {
                return !ShouldSuppressVisualTweens();
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldSuppressVisualTweens()
        {
            // v60: DOTween suppression is intentionally disabled. Keeping the method as a no-op
            // preserves compatibility with older helper code/settings while avoiding black-screen
            // playback and "You can't add elements to an inactive/killed Sequence" spam.
            return false;
        }

        private static bool ShouldForceHighDOTweenCapacity()
        {
            if (!TryGetInternalMacroSettings(out InternalMacroService? service, out InternalMacroSettings? settings))
            {
                return false;
            }

            // Reuse the old visual-tween setting as the high-capacity switch.
            // It no longer suppresses/kills DOTween; it only prevents repeated DOTween array growth.
            if (!settings.SuppressVisualTweensWhileInternalMacroRuns)
            {
                return false;
            }

            return settings.EnableInternalMacro || service.IsInternalMacroActive;
        }

        private static bool ShouldSuppressUnloadUnusedAssets()
        {
            if (!TryGetInternalMacroSettings(out InternalMacroService? service, out InternalMacroSettings? settings))
            {
                return false;
            }

            if (!settings.SuppressUnloadUnusedAssetsWhileInternalMacroRuns)
            {
                return false;
            }

            return settings.EnableInternalMacro || service.IsInternalMacroActive;
        }

        private static bool TryGetInternalMacroSettings(out InternalMacroService? service, out InternalMacroSettings? settings)
        {
            service = MainServiceField?.GetValue(null) as InternalMacroService;
            settings = service == null ? null : SettingsField?.GetValue(service) as InternalMacroSettings;
            return service != null && settings != null;
        }

        private static bool TryPatchOneShotAudio()
        {
            try
            {
                int patchedCount = 0;

                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip) }, nameof(AudioSourcePlayOneShotPrefix), ref patchedCount);
                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new[] { typeof(AudioClip), typeof(float) }, nameof(AudioSourcePlayOneShotVolumePrefix), ref patchedCount);

                // Some ADOFAI/renderer paths set a short clip on an AudioSource and call Play()/PlayDelayed()/PlayScheduled()
                // rather than PlayOneShot(). 4000+ KPS can exhaust Unity's virtual audio channels through those paths even
                // when MacroKeyViewer is disabled, so suppress short clip playback while the internal verification macro runs.
                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.Play), Type.EmptyTypes, nameof(AudioSourcePlayPrefix), ref patchedCount);
                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.Play), new[] { typeof(ulong) }, nameof(AudioSourcePlayDelayPrefix), ref patchedCount);
                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.PlayDelayed), new[] { typeof(float) }, nameof(AudioSourcePlayDelayedPrefix), ref patchedCount);
                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.PlayScheduled), new[] { typeof(double) }, nameof(AudioSourcePlayScheduledPrefix), ref patchedCount);

                // Also catch temporary AudioSource creation helpers.
                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.PlayClipAtPoint), new[] { typeof(AudioClip), typeof(Vector3) }, nameof(AudioSourcePlayClipAtPointPrefix), ref patchedCount);
                PatchAudioMethod(typeof(AudioSource), nameof(AudioSource.PlayClipAtPoint), new[] { typeof(AudioClip), typeof(Vector3), typeof(float) }, nameof(AudioSourcePlayClipAtPointVolumePrefix), ref patchedCount);

                return patchedCount > 0;
            }
            catch (Exception ex)
            {
                Debug.Log($"[Macro-Inserter] Short audio suppression patch failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static void PatchAudioMethod(Type type, string methodName, Type[] parameterTypes, string prefixName, ref int patchedCount)
        {
            MethodInfo? original = AccessTools.Method(type, methodName, parameterTypes);
            MethodInfo? prefix = AccessTools.Method(typeof(PseudoChordInputPlanFix), prefixName);
            if (original == null || prefix == null)
            {
                return;
            }

            Harmony.Patch(original, prefix: new HarmonyMethod(prefix));
            patchedCount++;
        }

        private static bool AudioSourcePlayOneShotPrefix(AudioClip clip)
        {
            return ShouldAllowOneShotAudio(clip);
        }

        private static bool AudioSourcePlayOneShotVolumePrefix(AudioClip clip, float volumeScale)
        {
            return ShouldAllowOneShotAudio(clip);
        }

        private static bool AudioSourcePlayPrefix(AudioSource __instance)
        {
            return ShouldAllowShortSourceAudio(__instance);
        }

        private static bool AudioSourcePlayDelayPrefix(AudioSource __instance, ulong delay)
        {
            return ShouldAllowShortSourceAudio(__instance);
        }

        private static bool AudioSourcePlayDelayedPrefix(AudioSource __instance, float delay)
        {
            return ShouldAllowShortSourceAudio(__instance);
        }

        private static bool AudioSourcePlayScheduledPrefix(AudioSource __instance, double time)
        {
            return ShouldAllowShortSourceAudio(__instance);
        }

        private static bool AudioSourcePlayClipAtPointPrefix(AudioClip clip, Vector3 position)
        {
            return ShouldAllowShortClipAudio(clip);
        }

        private static bool AudioSourcePlayClipAtPointVolumePrefix(AudioClip clip, Vector3 position, float volume)
        {
            return ShouldAllowShortClipAudio(clip);
        }

        private static bool ShouldAllowOneShotAudio(AudioClip clip)
        {
            try
            {
                if (!ShouldSuppressShortAudio(out _))
                {
                    return true;
                }

                // PlayOneShot is used for hit/UI one-shots; during verification playback it can be called thousands
                // of times per second. Block it wholesale while the internal macro is active.
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldAllowShortSourceAudio(AudioSource source)
        {
            try
            {
                if (source == null || source.clip == null)
                {
                    return true;
                }

                if (!ShouldSuppressShortAudio(out float maxClipSeconds))
                {
                    return true;
                }

                AudioClip clip = source.clip;
                if (clip.length <= maxClipSeconds)
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldAllowShortClipAudio(AudioClip clip)
        {
            try
            {
                if (clip == null)
                {
                    return true;
                }

                if (!ShouldSuppressShortAudio(out float maxClipSeconds))
                {
                    return true;
                }

                return clip.length > maxClipSeconds;
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldSuppressShortAudio(out float maxClipSeconds)
        {
            maxClipSeconds = 2.0f;

            InternalMacroService? service = MainServiceField?.GetValue(null) as InternalMacroService;
            if (service == null || !service.IsInternalMacroActive)
            {
                return false;
            }

            InternalMacroSettings? settings = SettingsField?.GetValue(service) as InternalMacroSettings;
            if (settings == null || !settings.SuppressOneShotAudioWhileInternalMacroRuns)
            {
                return false;
            }

            maxClipSeconds = Math.Max(0.01f, settings.SuppressShortAudioMaxClipSeconds);
            return true;
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

                if (settings.MaxHitsPerPlayerControlUpdate < 5000)
                {
                    int previousMaxHits = settings.MaxHitsPerPlayerControlUpdate;
                    settings.MaxHitsPerPlayerControlUpdate = 5000;
                    LogMinimal(log, $"Runtime input-pipeline plan active; raising MaxHitsPerPlayerControlUpdate from {previousMaxHits} to 5000 for dense directKeyTimes sections.");
                }

                // v63/v64: PerfectOverride verification can intentionally survive very large Unity stalls.
                // The previous v62 threshold only raised MaxLateRetryMs for >=30000 input entries,
                // so ~28k-entry maps could still hit tooLateSkipped after a single 250ms+ frame stall
                // and then appear to freeze with KPS=0. Raise the retry window for dense verification
                // plans so the scheduler drains the backlog instead of abandoning the stream.
                if (runtimeInputPlan.Count >= 10000 && settings.MaxLateRetryMs < 60000.0)
                {
                    double previousMaxLateRetryMs = settings.MaxLateRetryMs;
                    settings.MaxLateRetryMs = 60000.0;
                    LogMinimal(log, $"Runtime input-pipeline plan active; raising MaxLateRetryMs from {previousMaxLateRetryMs:F3}ms to 60000.000ms for dense PerfectOverride verification sections.");
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
            // Per-hit diagnostics are stored as numeric samples and dumped only when the scheduler stops.
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
                forceSimulation: asyncInputActive,
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

            // v54: 4000+ KPS cannot be represented as one managed Pulse() call per hit.
            // Preserve all counts/KPS, but coalesce dense pulses into per-key counter bumps.
            // This is not a stop/throttle: every hit still contributes to counters and pressed state.
            double durationSeconds = Math.Max(0, settings.MacroKeyViewerPulseMs) / 1000.0;
            int pulseCount = Math.Max(1, keyCount);
            int maxCalls = Math.Max(1, settings.MacroKeyViewerMaxPulseCallsPerEntry);
            bool coalesce = settings.EnableMacroKeyViewerPulseCoalescing || pulseCount > maxCalls;

            if (coalesce)
            {
                List<string> keyCycle = new();
                if (entry.AssignedKeyNames.Count > 0)
                {
                    keyCycle.AddRange(entry.AssignedKeyNames.Where(name => !string.IsNullOrWhiteSpace(name)));
                }
                else if (configuredKeys.Count > 0)
                {
                    int take = Math.Min(configuredKeys.Count, Math.Max(1, maxCalls));
                    for (int i = 0; i < take; i++)
                    {
                        int keyIndex = (int)((macroKeyViewerFallbackCounter + i) % configuredKeys.Count);
                        keyCycle.Add(configuredKeys[keyIndex]);
                    }
                    macroKeyViewerFallbackCounter += pulseCount;
                }

                if (keyCycle.Count == 0)
                {
                    return 0;
                }

                Dictionary<string, int> counts = new(StringComparer.Ordinal);
                for (int i = 0; i < pulseCount; i++)
                {
                    string keyName = keyCycle[i % keyCycle.Count];
                    counts.TryGetValue(keyName, out int oldCount);
                    counts[keyName] = oldCount + 1;
                }

                int represented = 0;
                foreach (KeyValuePair<string, int> pair in counts)
                {
                    try
                    {
                        service.MacroKeyViewer.Pulse(pair.Key, durationSeconds, pair.Value);
                        represented += pair.Value;
                    }
                    catch
                    {
                        break;
                    }
                }

                macroKeyViewerPulsesSinceSummary += represented;
                return represented;
            }

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

            // v47/v53: keep the 0.5s accounting window for deferred numeric
            // diagnostics, but do not emit a string from the input hot path.
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
            perfectOverrideSeenActive = false;
            hitErrorMeterResetForActiveRun = false;
            ResetStuckPlainSingle(-1);
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
