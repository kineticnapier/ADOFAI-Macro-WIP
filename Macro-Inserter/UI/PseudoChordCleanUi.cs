using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace Macro_Inserter;

internal static class PseudoChordCleanUi
{
    private const int KeyViewerColumns = 8;
    private const float KeyCellWidth = 58.0f;
    private const float KeyCellHeight = 48.0f;
    private const float RainHeight = 58.0f;

    private static readonly Dictionary<string, float> RainLevels = new Dictionary<string, float>(StringComparer.Ordinal);

    private static string offsetText = string.Empty;
    private static string maxLateRetryMsText = string.Empty;
    private static string foldDownMaxBpmText = string.Empty;
    private static string raiseUpMaxBpmText = string.Empty;
    private static string lagSpikeLogMsText = string.Empty;
    private static string rainGrowText = string.Empty;
    private static string rainDecayText = string.Empty;
    private static string macroKeyViewerPulseMsText = string.Empty;
    private static string virtualKeysText = string.Empty;
    private static float lastRainUpdateTime = -1.0f;
    private static GUIStyle? rainBoxStyle;

    public static bool MainLogPrefix()
    {
        NaturalFingeringOptions.Load();
        return NaturalFingeringOptions.LogMode != PseudoChordUiLogMode.None;
    }

    public static bool OnGuiPrefix(UnityModManager.ModEntry entry)
    {
        NaturalFingeringOptions.Load();
        if (!NaturalFingeringOptions.CleanUiEnabled)
        {
            return true;
        }

        try
        {
            object? settings = ReadStaticField("settings");
            object? service = ReadStaticField("service");
            if (settings == null)
            {
                return true;
            }

            DrawCleanUi(settings, service);
            return false;
        }
        catch (Exception ex)
        {
            GUILayout.BeginVertical("box");
            GUILayout.Label($"PseudoChord clean UI failed; falling back next frame. {ex.GetType().Name}: {ex.Message}");
            NaturalFingeringOptions.CleanUiEnabled = false;
            NaturalFingeringOptions.Save();
            GUILayout.EndVertical();
            return false;
        }
    }

    private static void DrawCleanUi(object settings, object? service)
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("Internal macro / directKeyTimes runtime input. Debug-only modes are hidden in this clean UI.");

        bool enableInternalMacro = GetBool(settings, "EnableInternalMacro", false);
        bool nextEnableInternalMacro = GUILayout.Toggle(enableInternalMacro, "EnableInternalMacro");
        SetBool(settings, "EnableInternalMacro", nextEnableInternalMacro);

        bool dryRun = GetBool(settings, "DryRun", false);
        SetBool(settings, "DryRun", GUILayout.Toggle(dryRun, "DryRun"));

        bool startFromCurrentFloor = GetBool(settings, "StartFromCurrentFloor", false);
        SetBool(settings, "StartFromCurrentFloor", GUILayout.Toggle(startFromCurrentFloor, "StartFromCurrentFloor"));

        DrawDoubleSetting(settings, "MacroOffsetMs", "MacroOffsetMs", ref offsetText, min: double.NegativeInfinity, max: double.PositiveInfinity, width: 120f);
        DrawClockMode(settings);
        DrawLogMode(settings);
        DrawDoubleSetting(settings, "MaxLateRetryMs", "MaxLateRetryMs", ref maxLateRetryMsText, min: 0.0, max: double.PositiveInfinity, width: 120f);

        ForceRuntimeSafeSettings(settings);

        DrawNaturalFingeringSettings();
        DrawLagSpikeSettings();
        DrawMacroKeyViewerSettings(settings, service);
        DrawServiceStats(service);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Stop scheduler", GUILayout.Width(160f)))
        {
            InvokeService(service, "Stop", "manual stop button");
        }

        if (GUILayout.Button("Warmup Macro", GUILayout.Width(160f)))
        {
            InvokeService(service, "Warmup");
        }

        GUILayout.EndHorizontal();

        bool cleanUiEnabled = GUILayout.Toggle(NaturalFingeringOptions.CleanUiEnabled, "Clean UI enabled / hide unused experimental controls");
        if (cleanUiEnabled != NaturalFingeringOptions.CleanUiEnabled)
        {
            NaturalFingeringOptions.CleanUiEnabled = cleanUiEnabled;
            NaturalFingeringOptions.Save();
        }

        GUILayout.EndVertical();
    }

    private static void DrawClockMode(object settings)
    {
        FieldInfo? field = AccessTools.Field(settings.GetType(), "ClockMode");
        if (field == null || !field.FieldType.IsEnum)
        {
            return;
        }

        string[] names = Enum.GetNames(field.FieldType);
        int selected = Math.Max(0, Array.IndexOf(names, (field.GetValue(settings)?.ToString() ?? names[0])));
        GUILayout.BeginHorizontal();
        GUILayout.Label("ClockMode", GUILayout.Width(140f));
        int next = GUILayout.Toolbar(selected, names, GUILayout.Width(Math.Max(240f, names.Length * 120f)));
        if (next >= 0 && next < names.Length && next != selected)
        {
            field.SetValue(settings, Enum.Parse(field.FieldType, names[next]));
        }

        GUILayout.EndHorizontal();
    }

    private static void DrawLogMode(object settings)
    {
        string[] names = { "None", "Minimal", "Normal", "Verbose" };
        int selected = (int)NaturalFingeringOptions.LogMode;
        selected = Math.Max(0, Math.Min(names.Length - 1, selected));
        GUILayout.BeginHorizontal();
        GUILayout.Label("LoggingMode", GUILayout.Width(140f));
        int next = GUILayout.Toolbar(selected, names, GUILayout.Width(440f));
        GUILayout.EndHorizontal();

        if (next != selected)
        {
            NaturalFingeringOptions.LogMode = (PseudoChordUiLogMode)next;
            NaturalFingeringOptions.Save();
        }

        FieldInfo? field = AccessTools.Field(settings.GetType(), "LoggingMode");
        if (field != null && field.FieldType.IsEnum)
        {
            string originalName = NaturalFingeringOptions.LogMode == PseudoChordUiLogMode.None
                ? "Minimal"
                : NaturalFingeringOptions.LogMode.ToString();
            if (Enum.GetNames(field.FieldType).Contains(originalName))
            {
                field.SetValue(settings, Enum.Parse(field.FieldType, originalName));
            }
        }
    }

    private static void DrawNaturalFingeringSettings()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("Natural fingering visual BPM thresholds");
        DrawOptionDouble("Fold down while BPM >", ref foldDownMaxBpmText, NaturalFingeringOptions.FoldDownMaxBpm, value => NaturalFingeringOptions.FoldDownMaxBpm = Math.Max(1.0, value));
        DrawOptionDouble("Raise low BPM while x2 <=", ref raiseUpMaxBpmText, NaturalFingeringOptions.RaiseUpMaxBpm, value => NaturalFingeringOptions.RaiseUpMaxBpm = Math.Max(1.0, value));
        GUILayout.Label($"Examples now depend on settings. Current: down<={NaturalFingeringOptions.FoldDownMaxBpm:F0}, raise<={NaturalFingeringOptions.RaiseUpMaxBpm:F0}.");
        GUILayout.EndVertical();
    }

    private static void DrawLagSpikeSettings()
    {
        GUILayout.BeginVertical("box");
        bool enable = GUILayout.Toggle(NaturalFingeringOptions.EnableLagSpikeLog, "Late-spike / lag-spike diagnostic log");
        if (enable != NaturalFingeringOptions.EnableLagSpikeLog)
        {
            NaturalFingeringOptions.EnableLagSpikeLog = enable;
            NaturalFingeringOptions.Save();
        }

        DrawOptionDouble("Log if processing exceeds ms", ref lagSpikeLogMsText, NaturalFingeringOptions.LagSpikeLogMs, value => NaturalFingeringOptions.LagSpikeLogMs = Math.Max(0.1, value));
        GUILayout.EndVertical();
    }

    private static void DrawMacroKeyViewerSettings(object settings, object? service)
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label("Macro KeyViewer");

        if (HasField(settings, "EnableMacroKeyViewer"))
        {
            bool enabled = GetBool(settings, "EnableMacroKeyViewer", true);
            SetBool(settings, "EnableMacroKeyViewer", GUILayout.Toggle(enabled, "EnableMacroKeyViewer"));
        }

        if (HasField(settings, "MacroKeyViewerKeysText"))
        {
            string keys = GetString(settings, "MacroKeyViewerKeysText", string.Empty);
            GUILayout.Label("MacroKeyViewerKeysText");
            string nextKeys = GUILayout.TextField(keys, GUILayout.Width(700f));
            if (!string.Equals(keys, nextKeys, StringComparison.Ordinal))
            {
                SetString(settings, "MacroKeyViewerKeysText", nextKeys);
            }
        }

        DrawDoubleSetting(settings, "MacroKeyViewerPulseMs", "PulseMs", ref macroKeyViewerPulseMsText, 0.0, 5000.0, 120f);

        bool rain = GUILayout.Toggle(NaturalFingeringOptions.EnableRain, "Rain view");
        if (rain != NaturalFingeringOptions.EnableRain)
        {
            NaturalFingeringOptions.EnableRain = rain;
            NaturalFingeringOptions.Save();
        }

        GUILayout.BeginHorizontal();
        DrawOptionDouble("Rain grow/s", ref rainGrowText, NaturalFingeringOptions.RainGrowPerSecond, value => NaturalFingeringOptions.RainGrowPerSecond = Math.Max(0.0, value), width: 90f);
        DrawOptionDouble("Rain decay/s", ref rainDecayText, NaturalFingeringOptions.RainDecayPerSecond, value => NaturalFingeringOptions.RainDecayPerSecond = Math.Max(0.0, value), width: 90f);
        GUILayout.EndHorizontal();

        DrawMacroKeyViewer(service, GetString(settings, "MacroKeyViewerKeysText", string.Empty));
        GUILayout.EndVertical();
    }

    private static void DrawMacroKeyViewer(object? service, string keysText)
    {
        if (service == null)
        {
            return;
        }

        object? viewer = AccessTools.Property(service.GetType(), "MacroKeyViewer")?.GetValue(service);
        if (viewer == null)
        {
            return;
        }

        MethodInfo? getSnapshot = AccessTools.Method(viewer.GetType(), "GetSnapshot", new[] { typeof(string) });
        if (getSnapshot == null)
        {
            return;
        }

        object? rawSnapshot = getSnapshot.Invoke(viewer, new object[] { keysText });
        if (rawSnapshot is not System.Collections.IEnumerable enumerable)
        {
            return;
        }

        PropertyInfo? kpsProperty = AccessTools.Property(viewer.GetType(), "Kps");
        float kps = 0.0f;
        object? kpsValue = kpsProperty?.GetValue(viewer);
        if (kpsValue != null)
        {
            try
            {
                kps = Convert.ToSingle(kpsValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                kps = 0.0f;
            }
        }

        List<object> snapshots = enumerable.Cast<object>().ToList();
        GUILayout.Label($"Macro KeyViewer KPS {kps:F0}");
        UpdateRain(snapshots);

        for (int row = 0; row * KeyViewerColumns < snapshots.Count; row++)
        {
            GUILayout.BeginHorizontal();
            for (int col = 0; col < KeyViewerColumns; col++)
            {
                int index = row * KeyViewerColumns + col;
                if (index >= snapshots.Count)
                {
                    break;
                }

                DrawKeyCell(snapshots[index]);
            }

            GUILayout.EndHorizontal();
        }
    }

    private static void UpdateRain(IReadOnlyList<object> snapshots)
    {
        float now = Time.unscaledTime;
        float delta = lastRainUpdateTime < 0.0f ? 0.0f : Mathf.Clamp(now - lastRainUpdateTime, 0.0f, 0.1f);
        lastRainUpdateTime = now;
        if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout)
        {
            return;
        }

        foreach (object snapshot in snapshots)
        {
            string name = ReadSnapshotString(snapshot, "Name", "<key>");
            bool pressed = ReadSnapshotBool(snapshot, "Pressed", false);
            RainLevels.TryGetValue(name, out float level);
            level = Mathf.Max(0.0f, level - delta * (float)NaturalFingeringOptions.RainDecayPerSecond);
            if (pressed)
            {
                level = Mathf.Min(RainHeight, level + 8.0f + delta * (float)NaturalFingeringOptions.RainGrowPerSecond);
            }

            RainLevels[name] = level;
        }
    }

    private static void DrawKeyCell(object snapshot)
    {
        string name = ReadSnapshotString(snapshot, "Name", "<key>");
        int count = ReadSnapshotInt(snapshot, "Count", 0);
        bool pressed = ReadSnapshotBool(snapshot, "Pressed", false);
        RainLevels.TryGetValue(name, out float rainLevel);

        GUILayout.BeginVertical(GUILayout.Width(KeyCellWidth));
        Rect rainRect = GUILayoutUtility.GetRect(KeyCellWidth, RainHeight, GUILayout.Width(KeyCellWidth), GUILayout.Height(RainHeight));
        GUI.Box(rainRect, GUIContent.none);
        if (NaturalFingeringOptions.EnableRain && rainLevel > 0.1f)
        {
            Color oldColor = GUI.color;
            GUI.color = new Color(0.55f, 0.12f, 0.95f, pressed ? 0.95f : 0.72f);
            Rect bar = new Rect(rainRect.x + 5.0f, rainRect.yMax - rainLevel, rainRect.width - 10.0f, rainLevel);
            GUI.Box(bar, GUIContent.none, GetRainBoxStyle());
            GUI.color = oldColor;
        }

        Color previous = GUI.color;
        if (pressed)
        {
            GUI.color = new Color(0.95f, 0.95f, 0.95f, 1.0f);
        }

        GUILayout.Box($"{name}\n{count}", GUILayout.Width(KeyCellWidth), GUILayout.Height(KeyCellHeight));
        GUI.color = previous;
        GUILayout.EndVertical();
    }

    private static GUIStyle GetRainBoxStyle()
    {
        if (rainBoxStyle != null)
        {
            return rainBoxStyle;
        }

        rainBoxStyle = new GUIStyle(GUI.skin.box)
        {
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(0, 0, 0, 0)
        };
        return rainBoxStyle;
    }

    private static void DrawServiceStats(object? service)
    {
        if (service == null)
        {
            return;
        }

        GUILayout.BeginVertical("box");
        GUILayout.Label(
            $"Offset adaptive={ReadDoubleProperty(service, "AdaptiveOffsetMs"):F3}ms effective={ReadDoubleProperty(service, "EffectiveOffsetMs"):F3}ms medianDispatchLag={ReadDoubleProperty(service, "MedianDispatchLagMs"):F3}ms");
        GUILayout.Label(
            $"Plan midspin={ReadIntProperty(service, "DetectedMidspinCount")} skippedDuplicateTime={ReadIntProperty(service, "SkippedDuplicateTimeCount")}");
        GUILayout.Label(
            $"Hit diff avg={ReadDoubleProperty(service, "AverageHitDiffMs"):F3}ms maxAbs={ReadDoubleProperty(service, "MaxAbsHitDiffMs"):F3}ms samples={ReadIntProperty(service, "HitDiffSampleCount")}");
        GUILayout.EndVertical();
    }

    private static void ForceRuntimeSafeSettings(object settings)
    {
        SetEnum(settings, "FireMode", "DirectHit");
        SetEnum(settings, "FirstHitMode", "Manual");
        SetBool(settings, "EnableHighDensityMode", true);
        SetBool(settings, "EnableHighDensityFastPath", false);
        SetBool(settings, "ExperimentalTimeSpoofForDirectHit", false);
        SetBool(settings, "EnableAdaptiveOffset", false);
        SetBool(settings, "ValidateAfterHit", false);
        if (HasField(settings, "MaxHitsPerPlayerControlUpdate") && GetInt(settings, "MaxHitsPerPlayerControlUpdate", 1) < 5000)
        {
            SetInt(settings, "MaxHitsPerPlayerControlUpdate", 5000);
        }
    }

    private static void DrawDoubleSetting(object target, string fieldName, string label, ref string text, double min, double max, float width)
    {
        if (!HasField(target, fieldName))
        {
            return;
        }

        double value = GetDouble(target, fieldName, 0.0);
        if (string.IsNullOrEmpty(text))
        {
            text = value.ToString(CultureInfo.InvariantCulture);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140f));
        text = GUILayout.TextField(text, GUILayout.Width(width));
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            SetDouble(target, fieldName, Math.Max(min, Math.Min(max, parsed)));
        }

        GUILayout.EndHorizontal();
    }

    private static void DrawOptionDouble(string label, ref string text, double value, Action<double> apply, float width = 120f)
    {
        if (string.IsNullOrEmpty(text))
        {
            text = value.ToString(CultureInfo.InvariantCulture);
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(190f));
        text = GUILayout.TextField(text, GUILayout.Width(width));
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            apply(parsed);
            NaturalFingeringOptions.Save();
        }

        GUILayout.EndHorizontal();
    }

    private static object? ReadStaticField(string fieldName)
    {
        FieldInfo? field = AccessTools.Field(typeof(Main), fieldName);
        return field?.GetValue(null);
    }

    private static bool HasField(object target, string fieldName)
    {
        return AccessTools.Field(target.GetType(), fieldName) != null;
    }

    private static bool GetBool(object target, string fieldName, bool fallback)
    {
        object? value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
        return value is bool boolValue ? boolValue : fallback;
    }

    private static void SetBool(object target, string fieldName, bool value)
    {
        AccessTools.Field(target.GetType(), fieldName)?.SetValue(target, value);
    }

    private static int GetInt(object target, string fieldName, int fallback)
    {
        object? value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
        return value is int intValue ? intValue : fallback;
    }

    private static void SetInt(object target, string fieldName, int value)
    {
        AccessTools.Field(target.GetType(), fieldName)?.SetValue(target, value);
    }

    private static double GetDouble(object target, string fieldName, double fallback)
    {
        object? value = AccessTools.Field(target.GetType(), fieldName)?.GetValue(target);
        if (value == null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static void SetDouble(object target, string fieldName, double value)
    {
        FieldInfo? field = AccessTools.Field(target.GetType(), fieldName);
        if (field == null)
        {
            return;
        }

        if (field.FieldType == typeof(float))
        {
            field.SetValue(target, (float)value);
            return;
        }

        field.SetValue(target, value);
    }

    private static string GetString(object target, string fieldName, string fallback)
    {
        return AccessTools.Field(target.GetType(), fieldName)?.GetValue(target)?.ToString() ?? fallback;
    }

    private static void SetString(object target, string fieldName, string value)
    {
        AccessTools.Field(target.GetType(), fieldName)?.SetValue(target, value);
    }

    private static void SetEnum(object target, string fieldName, string valueName)
    {
        FieldInfo? field = AccessTools.Field(target.GetType(), fieldName);
        if (field == null || !field.FieldType.IsEnum || !Enum.GetNames(field.FieldType).Contains(valueName))
        {
            return;
        }

        field.SetValue(target, Enum.Parse(field.FieldType, valueName));
    }

    private static void InvokeService(object? service, string methodName, params object[] args)
    {
        if (service == null)
        {
            return;
        }

        MethodInfo? method = AccessTools.Method(service.GetType(), methodName);
        method?.Invoke(service, args.Length == 0 ? null : args);
    }

    private static string ReadSnapshotString(object snapshot, string propertyName, string fallback)
    {
        return AccessTools.Property(snapshot.GetType(), propertyName)?.GetValue(snapshot)?.ToString() ?? fallback;
    }

    private static bool ReadSnapshotBool(object snapshot, string propertyName, bool fallback)
    {
        object? value = AccessTools.Property(snapshot.GetType(), propertyName)?.GetValue(snapshot);
        return value is bool boolValue ? boolValue : fallback;
    }

    private static int ReadSnapshotInt(object snapshot, string propertyName, int fallback)
    {
        object? value = AccessTools.Property(snapshot.GetType(), propertyName)?.GetValue(snapshot);
        if (value == null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static double ReadDoubleProperty(object target, string propertyName)
    {
        object? value = AccessTools.Property(target.GetType(), propertyName)?.GetValue(target);
        if (value == null)
        {
            return 0.0;
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0.0;
        }
    }

    private static int ReadIntProperty(object target, string propertyName)
    {
        object? value = AccessTools.Property(target.GetType(), propertyName)?.GetValue(target);
        if (value == null)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }
}
