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
    private static string virtualInputKeyCountText = "1";
    private static bool enabled;
    private static float lastOnUpdateExceptionLogTime = -10.0f;
    private static string? lastOnUpdateExceptionSignature;

    public static bool Load(UnityModManager.ModEntry entry)
    {
        modEntry = entry;
        settings = UnityModManager.ModSettings.Load<InternalMacroSettings>(entry);
        macroOffsetText = settings.MacroOffsetMs.ToString(CultureInfo.InvariantCulture);
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
            service?.Stop();
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
        settings.FireMode = (FireMode)GUILayout.Toolbar((int)settings.FireMode, new[] { "HitInputEvent", "DirectHit", "InputPatch" }, GUILayout.Width(360f));
        GUILayout.EndHorizontal();

        GUILayout.Label("HitInputEvent is the default Creplay-style path. DirectHit and InputPatch are fallback/experimental paths.");

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
            service?.Stop();
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
