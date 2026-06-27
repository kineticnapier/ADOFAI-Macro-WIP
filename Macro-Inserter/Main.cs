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
    private static string macroOffsetText = "0";
    private static string maxLateRetryMsText = "250";
    private static string virtualInputKeyCountText = "1";
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
        virtualInputKeyCountText = settings.VirtualInputKeyCount.ToString(CultureInfo.InvariantCulture);
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

        if (service != null)
        {
            GUILayout.Label($"Hit diff avg={service.AverageHitDiffMs:F3}ms maxAbs={service.MaxAbsHitDiffMs:F3}ms samples={service.HitDiffSampleCount}");
        }

        settings.ValidateAfterHit = GUILayout.Toggle(settings.ValidateAfterHit, "ValidateAfterHit");
        settings.DirectHitIgnoreInput = GUILayout.Toggle(settings.DirectHitIgnoreInput, "DirectHitIgnoreInput");

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

    private static void OnSaveGUI(UnityModManager.ModEntry entry)
    {
        settings.Save(entry);
    }

    private static void Log(string message)
    {
        modEntry?.Logger.Log($"[Macro-Inserter] {message}");
    }
}
