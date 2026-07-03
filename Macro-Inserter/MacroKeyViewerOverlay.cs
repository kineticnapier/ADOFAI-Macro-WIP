using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class MacroKeyViewerOverlay : MonoBehaviour
{
    private const string DefaultPressedColor = "#66D9FFFF";
    private const string DefaultIdleColor = "#202020DD";
    private const string DefaultTextColor = "#FFFFFFFF";
    private const string DefaultPanelColor = "#000000AA";

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
        Color pressedColor = ParseColor(macroSettings.MacroKeyViewerPressedColor, DefaultPressedColor);
        Color idleColor = ParseColor(macroSettings.MacroKeyViewerIdleColor, DefaultIdleColor);
        Color textColor = ParseColor(macroSettings.MacroKeyViewerTextColor, DefaultTextColor);
        Color panelColor = ParseColor(macroSettings.MacroKeyViewerPanelColor, DefaultPanelColor);

        DrawColoredRect(area, panelColor);
        GUILayout.BeginArea(area);
        DrawKeyViewerContent(state, keys, columns, keyWidth, keyHeight, scale, pressedColor, idleColor, textColor, panelColor);
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
        float scale,
        Color pressedColor,
        Color idleColor,
        Color textColor,
        Color panelColor)
    {
        GUIStyle titleStyle = new(GUI.skin.label)
        {
            fontSize = Mathf.Max(11, Mathf.RoundToInt(13f * scale))
        };
        titleStyle.normal.textColor = textColor;
        GUIStyle keyStyle = new(GUI.skin.label)
        {
            fontSize = Mathf.Max(10, Mathf.RoundToInt(12f * scale))
        };
        keyStyle.normal.textColor = textColor;
        GUIStyle countStyle = new(GUI.skin.label)
        {
            fontSize = Mathf.Max(9, Mathf.RoundToInt(10f * scale))
        };
        countStyle.normal.textColor = textColor;

        Color previousColor = GUI.color;
        Color previousBackground = GUI.backgroundColor;
        GUI.color = Color.white;
        GUI.backgroundColor = panelColor;

        GUILayout.BeginVertical("box");
        GUILayout.Label($"Macro KeyViewer  KPS {state.Kps:F0}", titleStyle);
        for (int index = 0; index < keys.Count; index += columns)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < columns && index + column < keys.Count; column++)
            {
                DrawMacroKey(keys[index + column], keyStyle, countStyle, keyWidth, keyHeight, pressedColor, idleColor);
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
        float height,
        Color pressedColor,
        Color idleColor)
    {
        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = key.Pressed ? pressedColor : idleColor;

        GUILayout.BeginVertical("box", GUILayout.Width(width), GUILayout.Height(height));
        GUILayout.Label(key.Name, keyStyle, GUILayout.Width(width - 8f));
        GUILayout.Label(key.Count.ToString(CultureInfo.InvariantCulture), countStyle, GUILayout.Width(width - 8f));
        GUILayout.EndVertical();

        GUI.backgroundColor = previousBackground;
    }

    private static Color ParseColor(string configuredColor, string defaultColor)
    {
        configuredColor ??= string.Empty;
        if (ColorUtility.TryParseHtmlString(configuredColor, out Color color))
        {
            return color;
        }

        return ColorUtility.TryParseHtmlString(defaultColor, out Color fallback)
            ? fallback
            : Color.white;
    }

    private static void DrawColoredRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }
}
