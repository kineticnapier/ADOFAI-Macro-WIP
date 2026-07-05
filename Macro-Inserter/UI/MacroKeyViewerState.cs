using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class MacroKeyViewerState
{
    private const float KpsWindowSeconds = 1.0f;

    private readonly List<KeySlot> keys = new();
    private readonly Queue<PulseBucket> recentPulseBuckets = new();
    private int recentPulseCount;
    private string configuredKeysText = string.Empty;

    public float Kps { get; private set; }

    public IReadOnlyList<string> ConfigureKeys(string keysText)
    {
        keysText ??= string.Empty;
        if (string.Equals(configuredKeysText, keysText, StringComparison.Ordinal))
        {
            return keys.Select(key => key.Name).ToArray();
        }

        string[] names = ParseKeyNames(keysText);
        Dictionary<string, KeySlot> existing = keys.ToDictionary(key => key.Name, StringComparer.Ordinal);
        keys.Clear();
        foreach (string name in names)
        {
            if (existing.TryGetValue(name, out KeySlot slot))
            {
                keys.Add(slot);
            }
            else
            {
                keys.Add(new KeySlot(name));
            }
        }

        configuredKeysText = keysText;
        return keys.Select(key => key.Name).ToArray();
    }

    public void Pulse(string keyName, double durationSeconds)
    {
        Pulse(keyName, durationSeconds, 1);
    }

    public void Pulse(string keyName, double durationSeconds, int count)
    {
        if (string.IsNullOrWhiteSpace(keyName) || count <= 0)
        {
            return;
        }

        float now = Time.unscaledTime;
        float duration = Mathf.Max(0.0f, (float)durationSeconds);
        KeySlot? key = keys.FirstOrDefault(slot => string.Equals(slot.Name, keyName, StringComparison.Ordinal));
        if (key == null)
        {
            key = new KeySlot(keyName.Trim());
            keys.Add(key);
        }

        key.PressedUntilTime = Math.Max(key.PressedUntilTime, now + duration);
        key.Count += count;
        recentPulseBuckets.Enqueue(new PulseBucket(now, count));
        recentPulseCount += count;
        TrimRecentPulseTimes(now);
    }

    public IReadOnlyList<MacroKeyViewerKeySnapshot> GetSnapshot(string keysText)
    {
        ConfigureKeys(keysText);
        float now = Time.unscaledTime;
        TrimRecentPulseTimes(now);
        return keys
            .Select(key => new MacroKeyViewerKeySnapshot(
                key.Name,
                key.Count,
                now <= key.PressedUntilTime))
            .ToArray();
    }

    public void ResetCounters()
    {
        foreach (KeySlot key in keys)
        {
            key.Count = 0;
            key.PressedUntilTime = 0.0f;
        }

        recentPulseBuckets.Clear();
        recentPulseCount = 0;
        Kps = 0.0f;
    }

    public static string[] ParseKeyNames(string keysText)
    {
        if (string.IsNullOrWhiteSpace(keysText))
        {
            return Array.Empty<string>();
        }

        return keysText
            .Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(key => key.Trim())
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private void TrimRecentPulseTimes(float now)
    {
        while (recentPulseBuckets.Count > 0 && now - recentPulseBuckets.Peek().Time > KpsWindowSeconds)
        {
            recentPulseCount -= recentPulseBuckets.Dequeue().Count;
        }

        if (recentPulseCount < 0)
        {
            recentPulseCount = 0;
        }

        Kps = recentPulseCount / KpsWindowSeconds;
    }

    private readonly struct PulseBucket
    {
        public PulseBucket(float time, int count)
        {
            Time = time;
            Count = count;
        }

        public float Time { get; }
        public int Count { get; }
    }

    private sealed class KeySlot
    {
        public KeySlot(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public float PressedUntilTime { get; set; }
        public int Count { get; set; }
    }
}

internal readonly struct MacroKeyViewerKeySnapshot
{
    public MacroKeyViewerKeySnapshot(string name, int count, bool pressed)
    {
        Name = name;
        Count = count;
        Pressed = pressed;
    }

    public string Name { get; }
    public int Count { get; }
    public bool Pressed { get; }
}
