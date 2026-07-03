using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class MacroKeyViewerOverlay : MonoBehaviour
{
    private static InternalMacroSettings? settings;
    private static Func<InternalMacroService?>? getService;
    private static Func<bool>? isModEnabled;

    public static MacroKeyViewerOverlay Create(
        InternalMacroSettings macroSettings,
        Func<InternalMacroService?> serviceAccessor,
        Func<bool> modEnabledAccessor)
    {
        settings = macroSettings;
        getService = serviceAccessor;
        isModEnabled = modEnabledAccessor;

        GameObject gameObject = new("Macro-Inserter KeyViewer Overlay");
        DontDestroyOnLoad(gameObject);
        return gameObject.AddComponent<MacroKeyViewerOverlay>();
    }

    public static void Configure(
        InternalMacroSettings macroSettings,
        Func<InternalMacroService?> serviceAccessor,
        Func<bool> modEnabledAccessor)
    {
        settings = macroSettings;
        getService = serviceAccessor;
        isModEnabled = modEnabledAccessor;
    }

    private void OnGUI()
    {
        if (!ShouldDraw(out InternalMacroSettings macroSettings, out MacroKeyViewerState state))
        {
            return;
        }

        DrawOverlay(macroSettings, state);
    }

    private static bool ShouldDraw(out InternalMacroSettings macroSettings, out MacroKeyViewerState state)
    {
        macroSettings = settings!;
        state = null!;

        if (settings == null ||
            getService == null ||
            isModEnabled == null ||
            !isModEnabled() ||
            !settings.EnableInternalMacro ||
            !settings.EnableMacroKeyViewer)
        {
            return false;
        }

        InternalMacroService? service = getService();
        if (service == null || !RuntimeSafety.IsAllowedPlaybackState())
        {
            return false;
        }

        macroSettings = settings;
        state = service.MacroKeyViewer;
        return true;
    }

    private static void DrawOverlay(InternalMacroSettings macroSettings, MacroKeyViewerState state)
    {
        float scale = Mathf.Clamp(macroSettings.MacroKeyViewerScale, 0.5f, 3.0f);
        IReadOnlyList<MacroKeyViewerKeySnapshot> keys = state.GetSnapshot(macroSettings.MacroKeyViewerKeysText);
        if (keys.Count == 0)
        {
            return;
        }

        int columns = Math.Max(1, Mathf.FloorToInt(8f / scale));
        float keyWidth = 46f * scale;
        float keyHeight = 40f * scale;
        float width = Mathf.Max(220f * scale, columns * (keyWidth + 8f) + 16f);
        int rows = Mathf.CeilToInt(keys.Count / (float)columns);
        float height = (24f * scale) + rows * (keyHeight + 6f) + 14f;
        float x = Mathf.Max(0.0f, macroSettings.MacroKeyViewerX);
        float y = ResolveY(macroSettings.MacroKeyViewerY, height);

        Rect area = new(x, y, width, height);
        GUILayout.BeginArea(area);
        DrawKeyViewerContent(state, keys, columns, keyWidth, keyHeight, scale);
        GUILayout.EndArea();
    }

    private static float ResolveY(float configuredY, float height)
    {
        float y = configuredY < 0.0f
            ? Screen.height + configuredY
            : configuredY;

        return Mathf.Clamp(y, 0.0f, Mathf.Max(0.0f, Screen.height - height));
    }

    private static void DrawKeyViewerContent(
        MacroKeyViewerState state,
        IReadOnlyList<MacroKeyViewerKeySnapshot> keys,
        int columns,
        float keyWidth,
        float keyHeight,
        float scale)
    {
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

        Color previousColor = GUI.color;
        Color previousBackground = GUI.backgroundColor;
        GUI.color = Color.white;

        GUILayout.BeginVertical("box");
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
        GUI.backgroundColor = previousBackground;
        GUI.color = previousColor;
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
            : new Color(0.35f, 0.35f, 0.35f, 0.85f);

        GUILayout.BeginVertical("box", GUILayout.Width(width), GUILayout.Height(height));
        GUILayout.Label(key.Name, keyStyle, GUILayout.Width(width - 8f));
        GUILayout.Label(key.Count.ToString(CultureInfo.InvariantCulture), countStyle, GUILayout.Width(width - 8f));
        GUILayout.EndVertical();

        GUI.backgroundColor = previousBackground;
    }
}
