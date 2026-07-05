using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace Macro_Inserter;

public static class Main
{
    private static InternalMacroSettings settings = new();
    private static InternalMacroService? service;
    private static Harmony? harmony;
    private static UnityModManager.ModEntry? modEntry;
    private static MacroKeyViewerOverlay? macroKeyViewerOverlay;
    private static string macroOffsetText = "0";
    private static string maxLateRetryMsText = "40";
    private static string maxHitsPerPlayerControlUpdateText = "8";
    private static string pseudoChordWindowMsText = "2";
    private static string pseudoChordMaxSpanMsText = "2";
    private static string pseudoChordExactDuplicateEpsilonMsText = "0.05";
    private static string maxHitsPerPseudoChordGroupText = "8";
    private static string virtualInputKeyCountText = "1";
    private static string macroKeyViewerPulseMsText = "80";
    private static string macroKeyViewerXText = "20";
    private static string macroKeyViewerYText = "-160";
    private static string macroKeyViewerScaleText = "1";
    private static string macroKeyViewerPressedColorText = "#66D9FFFF";
    private static string macroKeyViewerIdleColorText = "#202020DD";
    private static string macroKeyViewerTextColorText = "#FFFFFFFF";
    private static string macroKeyViewerPanelColorText = "#000000AA";
    private static string keyViewerRainPulseMsText = "70";
    private static string keyViewerRainSpeedPxPerSecText = "260";
    private static string keyViewerRainFadeMsText = "450";
    private static string keyViewerRainWidthScaleText = "0.72";
    private static string keyViewerRainMinHeightPxText = "8";
    private static string keyViewerRainMaxHeightPxText = "90";
    private static string keyViewerRainAlphaText = "0.72";
    private static string keyViewerRainColorText = "#66D9FFFF";
    private static string keyViewerRainMaxSegmentsText = "512";
    private static string keyViewerRainYOffsetPxText = "2";
    private static string naturalFingeringFoldDownMaxBpmText = "1000";
    private static string naturalFingeringRaiseUpMaxBpmText = "500";
    private static string fingeringNormalLogLimitText = "96";
    private static string fingeringVerboseLogLimitText = "384";
    private static string directKeyTimesLateSpikeLogMsText = "8";
    private static string directKeyTimesProcessingSpikeLogMsText = "8";
    private static string directKeyTimesSpikeLogMinIntervalMsText = "50";
    private static string directKeyTimesDeferredDumpEntriesText = "32";
    private static string cameraSafeMaxHitsPerPlayerControlUpdateText = "8";
    private static bool enabled;
    private static float lastOnUpdateExceptionLogTime = -10.0f;
    private static string? lastOnUpdateExceptionSignature;

    public static bool Load(UnityModManager.ModEntry entry)
    {
        modEntry = entry;
        RuntimeWarmup.TrySetDotweenCapacity();
        settings = UnityModManager.ModSettings.Load<InternalMacroSettings>(entry);
        macroOffsetText = settings.MacroOffsetMs.ToString(CultureInfo.InvariantCulture);
        maxLateRetryMsText = settings.MaxLateRetryMs.ToString(CultureInfo.InvariantCulture);
        maxHitsPerPlayerControlUpdateText = settings.MaxHitsPerPlayerControlUpdate.ToString(CultureInfo.InvariantCulture);
        pseudoChordWindowMsText = settings.PseudoChordWindowMs.ToString(CultureInfo.InvariantCulture);
        pseudoChordMaxSpanMsText = settings.PseudoChordMaxSpanMs.ToString(CultureInfo.InvariantCulture);
        pseudoChordExactDuplicateEpsilonMsText = settings.PseudoChordExactDuplicateEpsilonMs.ToString(CultureInfo.InvariantCulture);
        maxHitsPerPseudoChordGroupText = settings.MaxHitsPerPseudoChordGroup.ToString(CultureInfo.InvariantCulture);
        virtualInputKeyCountText = settings.VirtualInputKeyCount.ToString(CultureInfo.InvariantCulture);
        macroKeyViewerPulseMsText = settings.MacroKeyViewerPulseMs.ToString(CultureInfo.InvariantCulture);
        macroKeyViewerXText = settings.MacroKeyViewerX.ToString(CultureInfo.InvariantCulture);
        macroKeyViewerYText = settings.MacroKeyViewerY.ToString(CultureInfo.InvariantCulture);
        macroKeyViewerScaleText = settings.MacroKeyViewerScale.ToString(CultureInfo.InvariantCulture);
        macroKeyViewerPressedColorText = settings.MacroKeyViewerPressedColor ?? "#66D9FFFF";
        macroKeyViewerIdleColorText = settings.MacroKeyViewerIdleColor ?? "#202020DD";
        macroKeyViewerTextColorText = settings.MacroKeyViewerTextColor ?? "#FFFFFFFF";
        macroKeyViewerPanelColorText = settings.MacroKeyViewerPanelColor ?? "#000000AA";
        settings.MacroKeyViewerPressedColor = macroKeyViewerPressedColorText;
        settings.MacroKeyViewerIdleColor = macroKeyViewerIdleColorText;
        settings.MacroKeyViewerTextColor = macroKeyViewerTextColorText;
        settings.MacroKeyViewerPanelColor = macroKeyViewerPanelColorText;
        keyViewerRainPulseMsText = settings.KeyViewerRainPulseMs.ToString(CultureInfo.InvariantCulture);
        keyViewerRainSpeedPxPerSecText = settings.KeyViewerRainSpeedPxPerSec.ToString(CultureInfo.InvariantCulture);
        keyViewerRainFadeMsText = settings.KeyViewerRainFadeMs.ToString(CultureInfo.InvariantCulture);
        keyViewerRainWidthScaleText = settings.KeyViewerRainWidthScale.ToString(CultureInfo.InvariantCulture);
        keyViewerRainMinHeightPxText = settings.KeyViewerRainMinHeightPx.ToString(CultureInfo.InvariantCulture);
        keyViewerRainMaxHeightPxText = settings.KeyViewerRainMaxHeightPx.ToString(CultureInfo.InvariantCulture);
        keyViewerRainAlphaText = settings.KeyViewerRainAlpha.ToString(CultureInfo.InvariantCulture);
        keyViewerRainColorText = settings.KeyViewerRainColor ?? "#66D9FFFF";
        settings.KeyViewerRainColor = keyViewerRainColorText;
        keyViewerRainMaxSegmentsText = settings.KeyViewerRainMaxSegments.ToString(CultureInfo.InvariantCulture);
        keyViewerRainYOffsetPxText = settings.KeyViewerRainYOffsetPx.ToString(CultureInfo.InvariantCulture);
        settings.DirectKeyTimesDeferredDumpEntries = Math.Max(0, Math.Min(64, settings.DirectKeyTimesDeferredDumpEntries));
        directKeyTimesDeferredDumpEntriesText = settings.DirectKeyTimesDeferredDumpEntries.ToString(CultureInfo.InvariantCulture);
        settings.CameraSafeMaxHitsPerPlayerControlUpdate = Math.Max(1, Math.Min(128, settings.CameraSafeMaxHitsPerPlayerControlUpdate));
        cameraSafeMaxHitsPerPlayerControlUpdateText = settings.CameraSafeMaxHitsPerPlayerControlUpdate.ToString(CultureInfo.InvariantCulture);
        NaturalFingeringOptions.Load();
        settings.LoggingMode = NormalizeLoggingMode(settings.LoggingMode);
        NaturalFingeringOptions.LogMode = NaturalFingeringOptions.FromLoggingMode(settings.LoggingMode);
        InitializeDiagnosticTextFields();
        service = new InternalMacroService(settings, Log);
        macroKeyViewerOverlay = MacroKeyViewerOverlay.Create(settings, () => service, () => enabled);

        harmony = new Harmony(entry.Info.Id);
        InputPatches.Apply(harmony, Log, () => service);
        LifecyclePatches.Apply(harmony, Log, () => service);

        entry.OnToggle = OnToggle;
        entry.OnGUI = OnGUI;
        entry.OnSaveGUI = OnSaveGUI;
        entry.OnUpdate = OnUpdate;
        entry.OnUnload = OnUnload;

        Log("Loaded. Internal macro is disabled by default and intended for chart verification only.");
        return true;
    }

    private static bool OnUnload(UnityModManager.ModEntry entry)
    {
        if (macroKeyViewerOverlay != null)
        {
            UnityEngine.Object.Destroy(macroKeyViewerOverlay.gameObject);
            macroKeyViewerOverlay = null;
        }

        harmony?.UnpatchAll(entry.Info.Id);
        harmony = null;
        service?.Stop("mod unloaded");
        service = null;
        return true;
    }

    private static bool OnToggle(UnityModManager.ModEntry entry, bool value)
    {
        enabled = value;
        if (!enabled)
        {
            service?.Stop("settings disabled");
        }

        return true;
    }

    private static void OnUpdate(UnityModManager.ModEntry entry, float delta)
    {
        try
        {
            if (!enabled)
            {
                return;
            }

            service?.Tick();
        }
        catch (Exception ex)
        {
            LogOnUpdateException(ex);
        }
    }

    private static void LogOnUpdateException(Exception ex)
    {
        Exception root = ex.InnerException ?? ex;
        string signature = $"{ex.GetType().Name}:{root.GetType().Name}:{root.Message}";
        bool changed = !string.Equals(signature, lastOnUpdateExceptionSignature, StringComparison.Ordinal);
        if (!changed && Time.unscaledTime - lastOnUpdateExceptionLogTime < 1.0f)
        {
            return;
        }

        lastOnUpdateExceptionSignature = signature;
        lastOnUpdateExceptionLogTime = Time.unscaledTime;
        Log($"OnUpdate suppressed {ex.GetType().Name}: {root.GetType().Name}: {root.Message}");
    }

    private static void OnGUI(UnityModManager.ModEntry entry)
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("Internal macro is for chart verification only. Do not use it for competition, submission, or ranking purposes.");

        settings.EnableInternalMacro = GUILayout.Toggle(settings.EnableInternalMacro, "EnableInternalMacro");
        settings.DryRun = GUILayout.Toggle(settings.DryRun, "DryRun");
        settings.StartFromCurrentFloor = GUILayout.Toggle(settings.StartFromCurrentFloor, "StartFromCurrentFloor");

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroOffsetMs", GUILayout.Width(140f));
        macroOffsetText = GUILayout.TextField(macroOffsetText, GUILayout.Width(120f));
        if (double.TryParse(macroOffsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out double offset))
        {
            settings.MacroOffsetMs = offset;
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("ClockMode", GUILayout.Width(140f));
        settings.ClockMode = (ClockMode)GUILayout.Toolbar((int)settings.ClockMode, new[] { "Conductor", "AudioSource", "Unscaled" }, GUILayout.Width(360f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("FireMode", GUILayout.Width(140f));
        settings.FireMode = (FireMode)GUILayout.Toolbar((int)settings.FireMode, new[] { "HitInputEvent(Debug)", "DirectHit", "InputPatch(Exp)" }, GUILayout.Width(480f));
        GUILayout.EndHorizontal();

        GUILayout.Label("DirectHit is the normal internal path. HitInputEvent is debug/experimental; InputPatch is experimental and not recommended.");

        GUILayout.BeginHorizontal();
        GUILayout.Label("LoggingMode", GUILayout.Width(140f));
        int currentLogMode = Mathf.Clamp((int)settings.LoggingMode, 0, 3);
        int nextLogMode = GUILayout.Toolbar(currentLogMode, new[] { "None", "Minimal", "Normal", "Verbose" }, GUILayout.Width(480f));
        settings.LoggingMode = (LoggingMode)nextLogMode;
        NaturalFingeringOptions.LogMode = NaturalFingeringOptions.FromLoggingMode(settings.LoggingMode);
        GUILayout.EndHorizontal();

        DrawNaturalFingeringDiagnostics();
        DrawDeferredDiagnosticsSettings();

        GUILayout.BeginHorizontal();
        GUILayout.Label("FirstHitMode", GUILayout.Width(140f));
        settings.FirstHitMode = (FirstHitMode)GUILayout.Toolbar((int)settings.FirstHitMode, new[] { "Manual", "InputPatch", "HitInputEvent" }, GUILayout.Width(360f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("StateMode", GUILayout.Width(140f));
        settings.StateMode = (StateMode)GUILayout.Toolbar((int)settings.StateMode, new[] { "Default", "CapturedHumanState" }, GUILayout.Width(360f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("FailureMode", GUILayout.Width(140f));
        settings.FailureMode = (FailureMode)GUILayout.Toolbar((int)settings.FailureMode, new[] { "Stop", "Skip" }, GUILayout.Width(240f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MaxLateRetryMs", GUILayout.Width(140f));
        maxLateRetryMsText = GUILayout.TextField(maxLateRetryMsText, GUILayout.Width(120f));
        if (double.TryParse(maxLateRetryMsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double maxLateRetryMs))
        {
            settings.MaxLateRetryMs = Math.Max(0.0, maxLateRetryMs);
        }
        GUILayout.EndHorizontal();

        settings.EnableHighDensityMode = GUILayout.Toggle(settings.EnableHighDensityMode, "EnableHighDensityMode");
        GUILayout.BeginHorizontal();
        GUILayout.Label("MaxHits/Update", GUILayout.Width(140f));
        maxHitsPerPlayerControlUpdateText = GUILayout.TextField(maxHitsPerPlayerControlUpdateText, GUILayout.Width(120f));
        if (int.TryParse(maxHitsPerPlayerControlUpdateText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxHitsPerUpdate))
        {
            settings.MaxHitsPerPlayerControlUpdate = Math.Max(1, maxHitsPerUpdate);
        }
        GUILayout.EndHorizontal();

        settings.EnableCameraSafeMode = GUILayout.Toggle(settings.EnableCameraSafeMode, "EnableCameraSafeMode");
        settings.CameraSafeStrictMode = GUILayout.Toggle(settings.CameraSafeStrictMode, "CameraSafeStrictMode");
        settings.CameraSafeSplitInputGroups = GUILayout.Toggle(settings.CameraSafeSplitInputGroups, "CameraSafeSplitInputGroups");
        settings.CameraSafeQueueOnlyMode = GUILayout.Toggle(settings.CameraSafeQueueOnlyMode, "CameraSafeQueueOnlyMode");
        DrawIntSetting("CameraSafeMaxHits/Update", ref cameraSafeMaxHitsPerPlayerControlUpdateText, 1, 128, value => settings.CameraSafeMaxHitsPerPlayerControlUpdate = value);
        GUILayout.Label("Camera safe strict mode limits runtime directKeyTimes to 1 plan entry per PlayerControl_Update and splits grouped runtime inputs before fingering.");
        GUILayout.Label("QueueOnly mode avoids forced Simulated_PlayerControl_Update; queued keyTimes are consumed by the game update path instead.");

        GUILayout.BeginHorizontal();
        GUILayout.Label("PseudoChordWindowMs", GUILayout.Width(140f));
        pseudoChordWindowMsText = GUILayout.TextField(pseudoChordWindowMsText, GUILayout.Width(120f));
        if (double.TryParse(pseudoChordWindowMsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double pseudoChordWindowMs))
        {
            settings.PseudoChordWindowMs = Math.Max(0.0, pseudoChordWindowMs);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("PseudoChordMaxSpanMs", GUILayout.Width(140f));
        pseudoChordMaxSpanMsText = GUILayout.TextField(pseudoChordMaxSpanMsText, GUILayout.Width(120f));
        if (double.TryParse(pseudoChordMaxSpanMsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double pseudoChordMaxSpanMs))
        {
            settings.PseudoChordMaxSpanMs = Math.Max(0.0, pseudoChordMaxSpanMs);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("PseudoChordExactEpsMs", GUILayout.Width(140f));
        pseudoChordExactDuplicateEpsilonMsText = GUILayout.TextField(pseudoChordExactDuplicateEpsilonMsText, GUILayout.Width(120f));
        if (double.TryParse(pseudoChordExactDuplicateEpsilonMsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double pseudoChordExactDuplicateEpsilonMs))
        {
            settings.PseudoChordExactDuplicateEpsilonMs = Math.Max(0.0, pseudoChordExactDuplicateEpsilonMs);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MaxHits/PseudoChord", GUILayout.Width(140f));
        maxHitsPerPseudoChordGroupText = GUILayout.TextField(maxHitsPerPseudoChordGroupText, GUILayout.Width(120f));
        if (int.TryParse(maxHitsPerPseudoChordGroupText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maxHitsPerPseudoChordGroup))
        {
            settings.MaxHitsPerPseudoChordGroup = Math.Max(1, maxHitsPerPseudoChordGroup);
        }
        GUILayout.EndHorizontal();

        settings.ExperimentalTimeSpoofForDirectHit = GUILayout.Toggle(settings.ExperimentalTimeSpoofForDirectHit, "ExperimentalTimeSpoofForDirectHit");
        GUILayout.Label("TimeSpoof temporarily writes conductor songposition during DirectHit. Experimental; leave off unless testing ultra-high density.");

        if (service != null)
        {
            GUILayout.Label($"Offset base={settings.MacroOffsetMs:F3}ms adaptive={service.AdaptiveOffsetMs:F3}ms effective={service.EffectiveOffsetMs:F3}ms medianDispatchLag={service.MedianDispatchLagMs:F3}ms");
            GUILayout.Label($"Plan detected midspin={service.DetectedMidspinCount} skippedDuplicateTime={service.SkippedDuplicateTimeCount}");
            GUILayout.Label($"Hit diff avg={service.AverageHitDiffMs:F3}ms maxAbs={service.MaxAbsHitDiffMs:F3}ms samples={service.HitDiffSampleCount}");
        }

        settings.EnableAdaptiveOffset = GUILayout.Toggle(settings.EnableAdaptiveOffset, "EnableAdaptiveOffset");
        GUILayout.Label("AdaptiveOffset uses scheduler dispatch lag only; leave it off unless testing.");
        settings.ValidateAfterHit = GUILayout.Toggle(settings.ValidateAfterHit, "ValidateAfterHit");
        GUILayout.Label("ValidateAfterHit is for debugging only. Leave it off for normal runs.");
        settings.DirectHitIgnoreInput = GUILayout.Toggle(settings.DirectHitIgnoreInput, "DirectHitIgnoreInput");

        settings.EnableMacroKeyViewer = GUILayout.Toggle(settings.EnableMacroKeyViewer, "EnableMacroKeyViewer");
        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerKeys", GUILayout.Width(140f));
        settings.MacroKeyViewerKeysText = GUILayout.TextField(settings.MacroKeyViewerKeysText, GUILayout.Width(480f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerPulseMs", GUILayout.Width(140f));
        macroKeyViewerPulseMsText = GUILayout.TextField(macroKeyViewerPulseMsText, GUILayout.Width(120f));
        if (int.TryParse(macroKeyViewerPulseMsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pulseMs))
        {
            settings.MacroKeyViewerPulseMs = Math.Max(0, pulseMs);
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("Macro KeyViewer Rain");
        settings.EnableKeyViewerRain = GUILayout.Toggle(settings.EnableKeyViewerRain, "EnableKeyViewerRain");
        DrawFloatSetting("RainPulseMs", ref keyViewerRainPulseMsText, 5.0f, 300.0f, value => settings.KeyViewerRainPulseMs = value);
        DrawFloatSetting("RainSpeedPxPerSec", ref keyViewerRainSpeedPxPerSecText, 20.0f, 2000.0f, value => settings.KeyViewerRainSpeedPxPerSec = value);
        DrawFloatSetting("RainFadeMs", ref keyViewerRainFadeMsText, 0.0f, 3000.0f, value => settings.KeyViewerRainFadeMs = value);
        DrawFloatSetting("RainWidthScale", ref keyViewerRainWidthScaleText, 0.1f, 1.5f, value => settings.KeyViewerRainWidthScale = value);
        DrawFloatSetting("RainMinHeightPx", ref keyViewerRainMinHeightPxText, 1.0f, 80.0f, value =>
        {
            settings.KeyViewerRainMinHeightPx = value;
            settings.KeyViewerRainMaxHeightPx = Mathf.Max(settings.KeyViewerRainMaxHeightPx, value);
        });
        DrawFloatSetting("RainMaxHeightPx", ref keyViewerRainMaxHeightPxText, settings.KeyViewerRainMinHeightPx, 300.0f, value => settings.KeyViewerRainMaxHeightPx = Mathf.Max(settings.KeyViewerRainMinHeightPx, value));
        DrawFloatSetting("RainAlpha", ref keyViewerRainAlphaText, 0.0f, 1.0f, value => settings.KeyViewerRainAlpha = value);
        DrawStringSetting("RainColor", ref keyViewerRainColorText, value => settings.KeyViewerRainColor = value);
        DrawIntSetting("RainMaxSegments", ref keyViewerRainMaxSegmentsText, 32, 4096, value => settings.KeyViewerRainMaxSegments = value);
        DrawFloatSetting("RainYOffsetPx", ref keyViewerRainYOffsetPxText, -50.0f, 50.0f, value => settings.KeyViewerRainYOffsetPx = value);

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerX", GUILayout.Width(140f));
        macroKeyViewerXText = GUILayout.TextField(macroKeyViewerXText, GUILayout.Width(120f));
        if (float.TryParse(macroKeyViewerXText, NumberStyles.Float, CultureInfo.InvariantCulture, out float viewerX))
        {
            settings.MacroKeyViewerX = Math.Max(0.0f, viewerX);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerY", GUILayout.Width(140f));
        macroKeyViewerYText = GUILayout.TextField(macroKeyViewerYText, GUILayout.Width(120f));
        if (float.TryParse(macroKeyViewerYText, NumberStyles.Float, CultureInfo.InvariantCulture, out float viewerY))
        {
            settings.MacroKeyViewerY = viewerY;
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerScale", GUILayout.Width(140f));
        macroKeyViewerScaleText = GUILayout.TextField(macroKeyViewerScaleText, GUILayout.Width(120f));
        if (float.TryParse(macroKeyViewerScaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
        {
            settings.MacroKeyViewerScale = Mathf.Clamp(scale, 0.5f, 3.0f);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerPressedColor", GUILayout.Width(180f));
        macroKeyViewerPressedColorText = GUILayout.TextField(macroKeyViewerPressedColorText, GUILayout.Width(120f));
        settings.MacroKeyViewerPressedColor = macroKeyViewerPressedColorText;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerIdleColor", GUILayout.Width(180f));
        macroKeyViewerIdleColorText = GUILayout.TextField(macroKeyViewerIdleColorText, GUILayout.Width(120f));
        settings.MacroKeyViewerIdleColor = macroKeyViewerIdleColorText;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerTextColor", GUILayout.Width(180f));
        macroKeyViewerTextColorText = GUILayout.TextField(macroKeyViewerTextColorText, GUILayout.Width(120f));
        settings.MacroKeyViewerTextColor = macroKeyViewerTextColorText;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerPanelColor", GUILayout.Width(180f));
        macroKeyViewerPanelColorText = GUILayout.TextField(macroKeyViewerPanelColorText, GUILayout.Width(120f));
        settings.MacroKeyViewerPanelColor = macroKeyViewerPanelColorText;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Reset Macro KeyViewer", GUILayout.Width(180f)))
        {
            service?.ResetMacroKeyViewer();
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("VirtualInputKey", GUILayout.Width(140f));
        settings.VirtualInputKey = GUILayout.TextField(settings.VirtualInputKey, GUILayout.Width(120f));
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("VirtualInputKeyCount", GUILayout.Width(140f));
        virtualInputKeyCountText = GUILayout.TextField(virtualInputKeyCountText, GUILayout.Width(120f));
        if (int.TryParse(virtualInputKeyCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int keyCount))
        {
            settings.VirtualInputKeyCount = Math.Max(1, keyCount);
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Stop scheduler", GUILayout.Width(160f)))
        {
            service?.Stop("manual stop button");
        }

        if (GUILayout.Button("Warmup Macro", GUILayout.Width(160f)))
        {
            service?.Warmup();
        }

        GUILayout.EndVertical();
    }

    private static LoggingMode NormalizeLoggingMode(LoggingMode mode)
    {
        return Enum.IsDefined(typeof(LoggingMode), mode) ? mode : LoggingMode.Minimal;
    }

    private static void InitializeDiagnosticTextFields()
    {
        naturalFingeringFoldDownMaxBpmText = NaturalFingeringOptions.FoldDownMaxBpm.ToString(CultureInfo.InvariantCulture);
        naturalFingeringRaiseUpMaxBpmText = NaturalFingeringOptions.RaiseUpMaxBpm.ToString(CultureInfo.InvariantCulture);
        fingeringNormalLogLimitText = NaturalFingeringOptions.FingeringNormalLogLimit.ToString(CultureInfo.InvariantCulture);
        fingeringVerboseLogLimitText = NaturalFingeringOptions.FingeringVerboseLogLimit.ToString(CultureInfo.InvariantCulture);
        directKeyTimesLateSpikeLogMsText = NaturalFingeringOptions.LateSpikeLogMs.ToString(CultureInfo.InvariantCulture);
        directKeyTimesProcessingSpikeLogMsText = NaturalFingeringOptions.LagSpikeLogMs.ToString(CultureInfo.InvariantCulture);
        directKeyTimesSpikeLogMinIntervalMsText = NaturalFingeringOptions.SpikeLogMinIntervalMs.ToString(CultureInfo.InvariantCulture);
    }

    private static void DrawDeferredDiagnosticsSettings()
    {
        GUILayout.Label("Deferred directKeyTimes diagnostics");
        settings.DirectKeyTimesDumpOnlyOnFailure = GUILayout.Toggle(settings.DirectKeyTimesDumpOnlyOnFailure, "DumpOnlyOnFailure");
        settings.DirectKeyTimesDumpOnWin = GUILayout.Toggle(settings.DirectKeyTimesDumpOnWin, "DumpOnWin");
        DrawIntSetting("DeferredDumpEntries", ref directKeyTimesDeferredDumpEntriesText, 0, 64, value => settings.DirectKeyTimesDeferredDumpEntries = value);
        GUILayout.Label("v47/v48 keeps directKeyTimes logs out of the hot path; dump settings only affect stop/fail/win output.");
    }

    private static void DrawNaturalFingeringDiagnostics()
    {
        GUILayout.Label("Natural fingering / directKeyTimes diagnostics");

        NaturalFingeringOptions.EnableFingeringLog = GUILayout.Toggle(NaturalFingeringOptions.EnableFingeringLog, "EnableFingeringLog");
        DrawDoubleSetting("VisualBpmFoldDownMax", ref naturalFingeringFoldDownMaxBpmText, 1.0, 8000.0, value => NaturalFingeringOptions.FoldDownMaxBpm = value);
        DrawDoubleSetting("VisualBpmRaiseUpMax", ref naturalFingeringRaiseUpMaxBpmText, 1.0, 8000.0, value => NaturalFingeringOptions.RaiseUpMaxBpm = value);
        DrawIntSetting("FingeringNormalLogLimit", ref fingeringNormalLogLimitText, 0, 4096, value => NaturalFingeringOptions.FingeringNormalLogLimit = value);
        DrawIntSetting("FingeringVerboseLogLimit", ref fingeringVerboseLogLimitText, 0, 8192, value => NaturalFingeringOptions.FingeringVerboseLogLimit = value);

        NaturalFingeringOptions.EnableLateSpikeLog = GUILayout.Toggle(NaturalFingeringOptions.EnableLateSpikeLog, "EnableLateSpikeLog");
        DrawDoubleSetting("LateSpikeLogMs", ref directKeyTimesLateSpikeLogMsText, 0.1, 1000.0, value => NaturalFingeringOptions.LateSpikeLogMs = value);
        NaturalFingeringOptions.EnableLagSpikeLog = GUILayout.Toggle(NaturalFingeringOptions.EnableLagSpikeLog, "EnableProcessingSpikeLog");
        DrawDoubleSetting("ProcessingSpikeLogMs", ref directKeyTimesProcessingSpikeLogMsText, 0.1, 1000.0, value => NaturalFingeringOptions.LagSpikeLogMs = value);
        DrawDoubleSetting("SpikeLogMinIntervalMs", ref directKeyTimesSpikeLogMinIntervalMsText, 0.0, 5000.0, value => NaturalFingeringOptions.SpikeLogMinIntervalMs = value);
        GUILayout.Label("Fingering detail logs require LoggingMode Normal/Verbose. Spike logs require LoggingMode Minimal or higher.");
    }

    private static void DrawDoubleSetting(string label, ref string text, double min, double max, Action<double> apply)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180f));
        text = GUILayout.TextField(text, GUILayout.Width(120f));
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            apply(Math.Max(min, Math.Min(max, value)));
        }
        GUILayout.EndHorizontal();
    }

    private static void DrawFloatSetting(string label, ref string text, float min, float max, Action<float> apply)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180f));
        text = GUILayout.TextField(text, GUILayout.Width(120f));
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            apply(Mathf.Clamp(value, min, max));
        }
        GUILayout.EndHorizontal();
    }

    private static void DrawIntSetting(string label, ref string text, int min, int max, Action<int> apply)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180f));
        text = GUILayout.TextField(text, GUILayout.Width(120f));
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            apply(Mathf.Clamp(value, min, max));
        }
        GUILayout.EndHorizontal();
    }


    private static void DrawStringSetting(string label, ref string text, Action<string> apply)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180f));
        text = GUILayout.TextField(text, GUILayout.Width(120f));
        apply(text);
        GUILayout.EndHorizontal();
    }

    private static void OnSaveGUI(UnityModManager.ModEntry entry)
    {
        NaturalFingeringOptions.Save();
        settings.Save(entry);
    }

    private static void Log(string message)
    {
        modEntry?.Logger.Log($"[Macro-Inserter] {message}");
    }
}
