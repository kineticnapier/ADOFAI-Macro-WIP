using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Macro_Inserter;

internal static class MacroKeyViewerRainOverlay
{
    private const string DefaultRainColor = "#66D9FFFF";
    private static readonly Dictionary<string, KeyRainState> KeyStates = new(StringComparer.Ordinal);

    private static Texture2D? pixel;
    private static bool installLogged;
    private static float lastExceptionLogTime = -10.0f;
    private static string? lastExceptionSignature;

    public static void EnsureInstalled()
    {
        if (installLogged)
        {
            return;
        }

        installLogged = true;
        Debug.Log("[Macro-Inserter] MacroKeyViewer rain overlay v44 ready. UMM OnGUI is untouched.");
    }

    public static void Draw(
        InternalMacroSettings settings,
        IReadOnlyList<MacroKeyViewerKeySnapshot> snapshots,
        IReadOnlyList<Rect> keyRects)
    {
        try
        {
            DrawCore(settings, snapshots, keyRects);
        }
        catch (Exception ex)
        {
            LogException(ex);
        }
    }

    private static void DrawCore(
        InternalMacroSettings settings,
        IReadOnlyList<MacroKeyViewerKeySnapshot> snapshots,
        IReadOnlyList<Rect> keyRects)
    {
        if (!settings.EnableKeyViewerRain ||
            snapshots.Count == 0 ||
            keyRects.Count == 0 ||
            Event.current.type != EventType.Repaint)
        {
            return;
        }

        float now = Time.unscaledTime;
        UpdateSegments(settings, snapshots, now);
        DrawSegments(settings, snapshots, keyRects, now);
    }

    private static void UpdateSegments(
        InternalMacroSettings settings,
        IReadOnlyList<MacroKeyViewerKeySnapshot> snapshots,
        float now)
    {
        float pulseSeconds = Mathf.Clamp(settings.KeyViewerRainPulseMs, 5.0f, 300.0f) / 1000.0f;
        HashSet<string> activeNames = new(StringComparer.Ordinal);
        foreach (MacroKeyViewerKeySnapshot snapshot in snapshots)
        {
            string name = snapshot.Name;
            activeNames.Add(name);
            KeyRainState state = GetState(name);

            int countDelta = snapshot.Count - state.LastSeenCount;
            if (countDelta < 0)
            {
                state.Segments.Clear();
                state.ActiveSegment = null;
                countDelta = snapshot.Count > 0 && snapshot.Pressed ? 1 : 0;
            }

            int clampedDelta = Math.Min(countDelta, 32);
            for (int i = 0; i < clampedDelta; i++)
            {
                // Keep burst hits visually separated instead of stacking all bands on a single pixel.
                float startTime = now - (clampedDelta - i - 1) * 0.004f;
                RainSegment segment = new(startTime, pulseSeconds);
                ReleaseActiveSegment(state, now, settings);
                state.Segments.Add(segment);
                state.ActiveSegment = segment;
            }

            // RainingKeys-style fallback: key-down starts a band, key-up releases it.
            // This also covers frames where count-delta was missed but Pressed is visible.
            if (snapshot.Pressed)
            {
                if (!state.WasPressed && clampedDelta == 0)
                {
                    RainSegment segment = new(now, pulseSeconds);
                    ReleaseActiveSegment(state, now, settings);
                    state.Segments.Add(segment);
                    state.ActiveSegment = segment;
                }
                else if (state.ActiveSegment == null && state.Segments.Count > 0)
                {
                    RainSegment last = state.Segments[state.Segments.Count - 1];
                    if (!last.Released)
                    {
                        state.ActiveSegment = last;
                    }
                }
            }
            else
            {
                ReleaseActiveSegment(state, now, settings);
            }

            if (state.ActiveSegment != null && now - state.ActiveSegment.StartTime >= pulseSeconds)
            {
                ReleaseActiveSegment(state, state.ActiveSegment.StartTime + pulseSeconds, settings);
            }

            state.LastSeenCount = snapshot.Count;
            state.WasPressed = snapshot.Pressed;
        }

        foreach (KeyRainState state in KeyStates.Values)
        {
            TrimExpiredSegments(settings, state, now);
        }

        TrimTotalSegments(settings);

        if (KeyStates.Count > snapshots.Count + 16)
        {
            foreach (string key in KeyStates.Keys.Where(key => !activeNames.Contains(key)).ToArray())
            {
                KeyStates.Remove(key);
            }
        }
    }

    private static void ReleaseActiveSegment(KeyRainState state, float now, InternalMacroSettings settings)
    {
        if (state.ActiveSegment == null)
        {
            return;
        }

        if (!state.ActiveSegment.Released)
        {
            state.ActiveSegment.Release(now, settings);
        }

        state.ActiveSegment = null;
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

    private static void TrimExpiredSegments(InternalMacroSettings settings, KeyRainState state, float now)
    {
        state.Segments.RemoveAll(segment => segment.IsExpired(now, settings));
        if (state.ActiveSegment != null && !state.Segments.Contains(state.ActiveSegment))
        {
            state.ActiveSegment = null;
        }
    }

    private static void TrimTotalSegments(InternalMacroSettings settings)
    {
        int maxSegments = Mathf.Clamp(settings.KeyViewerRainMaxSegments, 32, 4096);
        int total = KeyStates.Values.Sum(state => state.Segments.Count);
        while (total > maxSegments)
        {
            KeyRainState? oldestState = null;
            float oldestStartTime = float.PositiveInfinity;
            foreach (KeyRainState state in KeyStates.Values)
            {
                if (state.Segments.Count == 0)
                {
                    continue;
                }

                float startTime = state.Segments[0].StartTime;
                if (startTime < oldestStartTime)
                {
                    oldestStartTime = startTime;
                    oldestState = state;
                }
            }

            if (oldestState == null)
            {
                return;
            }

            RainSegment removed = oldestState.Segments[0];
            oldestState.Segments.RemoveAt(0);
            if (ReferenceEquals(oldestState.ActiveSegment, removed))
            {
                oldestState.ActiveSegment = null;
            }

            total--;
        }
    }

    private static void DrawSegments(
        InternalMacroSettings settings,
        IReadOnlyList<MacroKeyViewerKeySnapshot> snapshots,
        IReadOnlyList<Rect> keyRects,
        float now)
    {
        Texture2D texture = GetPixelTexture();
        int count = Math.Min(snapshots.Count, keyRects.Count);
        float widthScale = Mathf.Clamp(settings.KeyViewerRainWidthScale, 0.1f, 1.5f);
        float minHeight = Mathf.Clamp(settings.KeyViewerRainMinHeightPx, 1.0f, 80.0f);
        float maxHeight = Mathf.Clamp(settings.KeyViewerRainMaxHeightPx, minHeight, 300.0f);
        float alphaScale = Mathf.Clamp01(settings.KeyViewerRainAlpha);
        float yOffset = Mathf.Clamp(settings.KeyViewerRainYOffsetPx, -50.0f, 50.0f);
        Color fallbackRainColor = ColorUtility.TryParseHtmlString(DefaultRainColor, out Color parsedFallback)
            ? parsedFallback
            : Color.cyan;
        Color pressedFallbackColor = ParseColor(settings.MacroKeyViewerPressedColor, fallbackRainColor);
        Color baseColor = ParseColor(settings.KeyViewerRainColor, pressedFallbackColor);

        int oldDepth = GUI.depth;
        GUI.depth = Math.Min(oldDepth, -10000);
        try
        {
            for (int i = 0; i < count; i++)
            {
                MacroKeyViewerKeySnapshot snapshot = snapshots[i];
                if (!KeyStates.TryGetValue(snapshot.Name, out KeyRainState state) || state.Segments.Count == 0)
                {
                    continue;
                }

                Rect keyRect = keyRects[i];
                if (keyRect.width <= 1.0f || keyRect.height <= 1.0f)
                {
                    continue;
                }

                float width = Mathf.Max(1.0f, keyRect.width * widthScale);
                float x = keyRect.x + (keyRect.width - width) * 0.5f;
                float anchorY = keyRect.y - yOffset;

                foreach (RainSegment segment in state.Segments)
                {
                    float alpha = segment.Alpha(now, settings) * alphaScale;
                    if (alpha <= 0.01f)
                    {
                        continue;
                    }

                    float height = segment.Height(now, settings, minHeight, maxHeight);
                    float offset = segment.ScrollOffset(now, settings);
                    float bottom = anchorY - offset;
                    Rect rect = new(x, bottom - height, width, height);
                    Color color = baseColor;
                    color.a *= segment.Released ? alpha * 0.78f : alpha;
                    DrawRect(rect, color, texture);
                }
            }
        }
        finally
        {
            GUI.depth = oldDepth;
        }
    }

    private static void DrawRect(Rect rect, Color color, Texture2D texture)
    {
        if (rect.height <= 0.5f || rect.width <= 0.5f)
        {
            return;
        }

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

    private static Color ParseColor(string? configuredColor, Color fallback)
    {
        configuredColor ??= string.Empty;
        return ColorUtility.TryParseHtmlString(configuredColor, out Color color)
            ? color
            : fallback;
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
        Debug.Log($"[Macro-Inserter] MacroKeyViewer rain overlay suppressed {signature}");
    }

    private sealed class KeyRainState
    {
        public int LastSeenCount { get; set; }
        public bool WasPressed { get; set; }
        public RainSegment? ActiveSegment { get; set; }
        public List<RainSegment> Segments { get; } = new();
    }

    private sealed class RainSegment
    {
        private readonly float activeSeconds;
        private float releaseTime;
        private float releaseHeight;

        public RainSegment(float startTime, float activeSeconds)
        {
            StartTime = startTime;
            this.activeSeconds = Math.Max(0.001f, activeSeconds);
            releaseTime = -1.0f;
            releaseHeight = 0.0f;
        }

        public float StartTime { get; }
        public bool Released => releaseTime >= 0.0f;

        public void Release(float now, InternalMacroSettings settings)
        {
            if (Released)
            {
                return;
            }

            releaseTime = Math.Max(StartTime, now);
            float minHeight = Mathf.Clamp(settings.KeyViewerRainMinHeightPx, 1.0f, 80.0f);
            float maxHeight = Mathf.Clamp(settings.KeyViewerRainMaxHeightPx, minHeight, 300.0f);
            releaseHeight = Height(releaseTime, settings, minHeight, maxHeight);
        }

        public float Height(float now, InternalMacroSettings settings, float minHeight, float maxHeight)
        {
            if (Released)
            {
                return Mathf.Clamp(releaseHeight, minHeight, maxHeight);
            }

            // RainingKeys style: while the key is down, the band grows at rain speed.
            float speed = Mathf.Clamp(settings.KeyViewerRainSpeedPxPerSec, 20.0f, 2000.0f);
            float activeAge = Mathf.Max(0.0f, now - StartTime);
            return Mathf.Clamp(minHeight + activeAge * speed, minHeight, maxHeight);
        }

        public float ScrollOffset(float now, InternalMacroSettings settings)
        {
            if (!Released)
            {
                return 0.0f;
            }

            float speed = Mathf.Clamp(settings.KeyViewerRainSpeedPxPerSec, 20.0f, 2000.0f);
            return Mathf.Max(0.0f, now - releaseTime) * speed;
        }

        public float Alpha(float now, InternalMacroSettings settings)
        {
            if (now < StartTime)
            {
                return 0.0f;
            }

            if (!Released)
            {
                return Mathf.Clamp01((now - StartTime) / 0.015f);
            }

            float fadeSeconds = FadeSeconds(settings);
            if (fadeSeconds <= 0.0001f)
            {
                return 0.0f;
            }

            return Mathf.Clamp01(1.0f - (now - releaseTime) / fadeSeconds);
        }

        public bool IsExpired(float now, InternalMacroSettings settings)
        {
            if (!Released)
            {
                return false;
            }

            float fadeSeconds = FadeSeconds(settings);
            if (fadeSeconds <= 0.0001f)
            {
                return true;
            }

            return now - releaseTime > fadeSeconds;
        }

        private static float FadeSeconds(InternalMacroSettings settings)
        {
            return Mathf.Clamp(settings.KeyViewerRainFadeMs, 0.0f, 3000.0f) / 1000.0f;
        }
    }
}
