using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Macro_Inserter;

internal static class MacroKeyViewerRainOverlay
{
    private const int Columns = 8;
    private const float CellWidth = 52.0f;
    private const float CellHeight = 39.0f;
    private const float CellGap = 4.0f;
    private const float RainHeight = 135.0f;
    private const float LeftMargin = 4.0f;
    private const float BottomMargin = 4.0f;
    private const float BarInsetX = 9.0f;
    private const float MinActiveHeight = 6.0f;
    private const float MaxActiveHeight = 46.0f;
    private const float ReleasedScrollSpeed = 145.0f;
    private const float ReleasedFadeSeconds = 0.75f;
    private const int MaxSegmentsPerKey = 96;

    private static readonly FieldInfo? ServiceField = AccessTools.Field(typeof(Main), "service");
    private static readonly FieldInfo? SettingsField = AccessTools.Field(typeof(InternalMacroService), "settings");
    private static readonly Dictionary<string, KeyRainState> KeyStates = new(StringComparer.Ordinal);

    private static GameObject? host;
    private static Texture2D? pixel;
    private static float lastExceptionLogTime = -10.0f;
    private static string? lastExceptionSignature;

    public static void EnsureInstalled()
    {
        try
        {
            if (host != null)
            {
                return;
            }

            host = new GameObject("Macro-Inserter KV Rain Overlay");
            host.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Behaviour>();
            Debug.Log("[Macro-Inserter] MacroKeyViewer rain overlay v42 installed. UMM OnGUI is untouched.");
        }
        catch (Exception ex)
        {
            Debug.Log($"[Macro-Inserter] MacroKeyViewer rain overlay v42 install failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Draw()
    {
        NaturalFingeringOptions.Load();
        if (!NaturalFingeringOptions.EnableRain)
        {
            return;
        }

        InternalMacroService? service = ServiceField?.GetValue(null) as InternalMacroService;
        if (service == null)
        {
            return;
        }

        object? settings = SettingsField?.GetValue(service);
        if (settings == null || !ReadBool(settings, "EnableMacroKeyViewer", true))
        {
            return;
        }

        string keysText = ReadString(settings, "MacroKeyViewerKeysText", string.Empty);
        double pulseSeconds = Math.Max(0.001, ReadDouble(settings, "MacroKeyViewerPulseMs", 80.0) / 1000.0);
        MacroKeyViewerKeySnapshot[] snapshots = service.MacroKeyViewer.GetSnapshot(keysText).ToArray();
        if (snapshots.Length == 0)
        {
            return;
        }

        float now = Time.unscaledTime;
        UpdateSegments(snapshots, now, (float)pulseSeconds);

        if (Event.current.type != EventType.Repaint)
        {
            return;
        }

        DrawSegments(snapshots, now, (float)pulseSeconds);
    }

    private static void UpdateSegments(IReadOnlyList<MacroKeyViewerKeySnapshot> snapshots, float now, float pulseSeconds)
    {
        HashSet<string> activeNames = new(StringComparer.Ordinal);
        foreach (MacroKeyViewerKeySnapshot snapshot in snapshots)
        {
            string name = snapshot.Name;
            activeNames.Add(name);
            KeyRainState state = GetState(name);

            int countDelta = snapshot.Count - state.LastSeenCount;
            if (countDelta < 0)
            {
                // KeyViewer counters were reset, usually because the scheduler restarted.
                state.Segments.Clear();
                countDelta = snapshot.Count;
            }

            for (int i = 0; i < Math.Min(countDelta, 8); i++)
            {
                float startOffset = countDelta <= 1 ? 0.0f : i * 0.004f;
                state.Segments.Add(new RainSegment(now + startOffset, pulseSeconds));
            }

            state.LastSeenCount = snapshot.Count;
            TrimSegments(state, now);
        }

        if (KeyStates.Count > snapshots.Count + 16)
        {
            foreach (string key in KeyStates.Keys.Where(key => !activeNames.Contains(key)).ToArray())
            {
                KeyStates.Remove(key);
            }
        }
    }

    private static KeyRainState GetState(string keyName)
    {
        if (!KeyStates.TryGetValue(keyName, out KeyRainState state))
        {
            state = new KeyRainState();
            KeyStates[keyName] = state;
        }

        return state;
    }

    private static void TrimSegments(KeyRainState state, float now)
    {
        state.Segments.RemoveAll(segment => segment.Alpha(now) <= 0.01f || segment.TopY(now) > RainHeight + 60.0f);
        if (state.Segments.Count <= MaxSegmentsPerKey)
        {
            return;
        }

        int removeCount = state.Segments.Count - MaxSegmentsPerKey;
        state.Segments.RemoveRange(0, removeCount);
    }

    private static void DrawSegments(IReadOnlyList<MacroKeyViewerKeySnapshot> snapshots, float now, float pulseSeconds)
    {
        Texture2D texture = GetPixelTexture();
        int rows = Math.Max(1, (snapshots.Count + Columns - 1) / Columns);
        float startX = LeftMargin;
        float baseY = Screen.height - BottomMargin - rows * (CellHeight + CellGap);

        for (int i = 0; i < snapshots.Count; i++)
        {
            MacroKeyViewerKeySnapshot snapshot = snapshots[i];
            if (!KeyStates.TryGetValue(snapshot.Name, out KeyRainState state) || state.Segments.Count == 0)
            {
                continue;
            }

            int col = i % Columns;
            int row = i / Columns;
            float x = startX + col * (CellWidth + CellGap) + BarInsetX;
            float keyTop = baseY + row * (CellHeight + CellGap);
            float width = CellWidth - BarInsetX * 2.0f;

            foreach (RainSegment segment in state.Segments)
            {
                float alpha = segment.Alpha(now);
                if (alpha <= 0.01f)
                {
                    continue;
                }

                float height = segment.Height(now);
                float offset = segment.ScrollOffset(now);
                float bottom = keyTop - offset;
                float y = bottom - height;
                Rect rect = new Rect(x, y, width, height);
                Color color = segment.IsActive(now)
                    ? new Color(0.62f, 0.18f, 1.0f, 0.90f * alpha)
                    : new Color(0.55f, 0.12f, 0.95f, 0.62f * alpha);
                DrawRect(rect, color, texture);
            }
        }
    }

    private static void DrawRect(Rect rect, Color color, Texture2D texture)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, texture);
        GUI.color = old;
    }

    private static Texture2D GetPixelTexture()
    {
        if (pixel != null)
        {
            return pixel;
        }

        pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        pixel.SetPixel(0, 0, Color.white);
        pixel.Apply(false, true);
        return pixel;
    }

    private static bool ReadBool(object target, string fieldName, bool fallback)
    {
        FieldInfo? field = AccessTools.Field(target.GetType(), fieldName);
        if (field == null || field.GetValue(target) is not bool value)
        {
            return fallback;
        }

        return value;
    }

    private static string ReadString(object target, string fieldName, string fallback)
    {
        FieldInfo? field = AccessTools.Field(target.GetType(), fieldName);
        return field?.GetValue(target) as string ?? fallback;
    }

    private static double ReadDouble(object target, string fieldName, double fallback)
    {
        FieldInfo? field = AccessTools.Field(target.GetType(), fieldName);
        object? raw = field?.GetValue(target);
        return raw switch
        {
            double value => value,
            float value => value,
            int value => value,
            _ => fallback
        };
    }

    private static void LogException(Exception ex)
    {
        Exception root = ex.InnerException ?? ex;
        string signature = $"{ex.GetType().Name}:{root.GetType().Name}:{root.Message}";
        bool changed = !string.Equals(signature, lastExceptionSignature, StringComparison.Ordinal);
        if (!changed && Time.unscaledTime - lastExceptionLogTime < 2.0f)
        {
            return;
        }

        lastExceptionSignature = signature;
        lastExceptionLogTime = Time.unscaledTime;
        Debug.Log($"[Macro-Inserter] MacroKeyViewer rain overlay v42 suppressed {signature}");
    }

    private sealed class KeyRainState
    {
        public int LastSeenCount { get; set; }
        public List<RainSegment> Segments { get; } = new();
    }

    private readonly struct RainSegment
    {
        private readonly float startTime;
        private readonly float activeSeconds;

        public RainSegment(float startTime, float activeSeconds)
        {
            this.startTime = startTime;
            this.activeSeconds = Math.Max(0.001f, activeSeconds);
        }

        private float ReleaseTime => startTime + activeSeconds;

        public bool IsActive(float now)
        {
            return now < ReleaseTime;
        }

        public float Height(float now)
        {
            float activeT = Mathf.Clamp01((now - startTime) / activeSeconds);
            return Mathf.Lerp(MinActiveHeight, MaxActiveHeight, activeT);
        }

        public float ScrollOffset(float now)
        {
            if (now <= ReleaseTime)
            {
                return 0.0f;
            }

            return (now - ReleaseTime) * ReleasedScrollSpeed;
        }

        public float Alpha(float now)
        {
            if (now <= ReleaseTime)
            {
                return Mathf.Clamp01((now - startTime) / 0.015f);
            }

            return Mathf.Clamp01(1.0f - (now - ReleaseTime) / ReleasedFadeSeconds);
        }

        public float TopY(float now)
        {
            return ScrollOffset(now) + Height(now);
        }
    }

    private sealed class Behaviour : MonoBehaviour
    {
        private void OnGUI()
        {
            try
            {
                Draw();
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }
    }
}
