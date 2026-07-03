using System;
using System.Collections.Generic;
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
    private static string macroOffsetText = "0";
    private static string maxLateRetryMsText = "40";
    private static string maxHitsPerPlayerControlUpdateText = "8";
    private static string virtualInputKeyCountText = "1";
    private static string macroKeyViewerPulseMsText = "80";
    private static string macroKeyViewerScaleText = "1";
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
        virtualInputKeyCountText = settings.VirtualInputKeyCount.ToString(CultureInfo.InvariantCulture);
        macroKeyViewerPulseMsText = settings.MacroKeyViewerPulseMs.ToString(CultureInfo.InvariantCulture);
        macroKeyViewerScaleText = settings.MacroKeyViewerScale.ToString(CultureInfo.InvariantCulture);
        service = new InternalMacroService(settings, Log);

        harmony = new Harmony(entry.Info.Id);
        InputPatches.Apply(harmony, Log, () => service);
        LifecyclePatches.Apply(harmony, Log, () => service);

        entry.OnToggle = OnToggle;
        entry.OnGUI = OnGUI;
        entry.OnSaveGUI = OnSaveGUI;
        entry.OnUpdate = OnUpdate;

        Log("Loaded. Internal macro is disabled by default and intended for chart verification only.");
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
        settings.LoggingMode = (LoggingMode)GUILayout.Toolbar((int)settings.LoggingMode, new[] { "Minimal", "Normal", "Verbose" }, GUILayout.Width(360f));
        GUILayout.EndHorizontal();

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

        GUILayout.BeginHorizontal();
        GUILayout.Label("MacroKeyViewerScale", GUILayout.Width(140f));
        macroKeyViewerScaleText = GUILayout.TextField(macroKeyViewerScaleText, GUILayout.Width(120f));
        if (float.TryParse(macroKeyViewerScaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
        {
            settings.MacroKeyViewerScale = Mathf.Clamp(scale, 0.5f, 3.0f);
        }
        GUILayout.EndHorizontal();

        if (settings.EnableMacroKeyViewer && service != null)
        {
            DrawMacroKeyViewer(service.MacroKeyViewer);
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

    private static void DrawMacroKeyViewer(MacroKeyViewerState state)
    {
        float scale = Mathf.Clamp(settings.MacroKeyViewerScale, 0.5f, 3.0f);
        IReadOnlyList<MacroKeyViewerKeySnapshot> keys = state.GetSnapshot(settings.MacroKeyViewerKeysText);
        if (keys.Count == 0)
        {
            return;
        }

        GUIStyle titleStyle = new(GUI.skin.label)
        {
            fontSize = Mathf.Max(11, Mathf.RoundToInt(13f * scale))
        };
        GUIStyle keyStyle = new(GUI.skin.label)
        {
            fontSize = Mathf.Max(10, Mathf.RoundToInt(12f * scale))
        };
        GUIStyle countStyle = new(GUI.skin.label)
        {
            fontSize = Mathf.Max(9, Mathf.RoundToInt(10f * scale))
        };

        int columns = Math.Max(1, Mathf.FloorToInt(8f / scale));
        float keyWidth = 46f * scale;
        float keyHeight = 40f * scale;

        GUILayout.BeginVertical("box", GUILayout.Width(Mathf.Max(220f, columns * (keyWidth + 8f) + 16f)));
        GUILayout.Label($"Macro KeyViewer  KPS {state.Kps:F0}", titleStyle);
        for (int index = 0; index < keys.Count; index += columns)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < columns && index + column < keys.Count; column++)
            {
                DrawMacroKey(keys[index + column], keyStyle, countStyle, keyWidth, keyHeight);
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
    }

    private static void DrawMacroKey(
        MacroKeyViewerKeySnapshot key,
        GUIStyle keyStyle,
        GUIStyle countStyle,
        float width,
        float height)
    {
        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = key.Pressed
            ? new Color(0.4f, 0.85f, 1.0f, 1.0f)
            : new Color(0.35f, 0.35f, 0.35f, 1.0f);

        GUILayout.BeginVertical("box", GUILayout.Width(width), GUILayout.Height(height));
        GUILayout.Label(key.Name, keyStyle, GUILayout.Width(width - 8f));
        GUILayout.Label(key.Count.ToString(CultureInfo.InvariantCulture), countStyle, GUILayout.Width(width - 8f));
        GUILayout.EndVertical();
        GUI.backgroundColor = previousBackground;
    }

    private static void OnSaveGUI(UnityModManager.ModEntry entry)
    {
        settings.Save(entry);
    }

    private static void Log(string message)
    {
        modEntry?.Logger.Log($"[Macro-Inserter] {message}");
    }
}
