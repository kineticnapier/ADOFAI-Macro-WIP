using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class MacroPlanBuilder
{
    private const double DuplicateTargetTimeThresholdSeconds = 0.001;
    private const double SpeedChangeRatioThreshold = 4.0;
    private const double UltraFastIntervalMs = 5.0;
    private const double FastIntervalMs = 30.0;

    private readonly Action<string> log;

    public MacroPlanBuilder(Action<string> log)
    {
        this.log = log;
    }

    public MacroPlanBuildResult Build(double macroOffsetMs, bool logPreview = true)
    {
        string? floorSourceFailure = null;
        IReadOnlyList<object> floors = GetFloorsFromLevelMaker(out floorSourceFailure);
        if (floors.Count == 0)
        {
            floors = GetFloorsFromScene(out string? fallbackFailure);
            if (floors.Count == 0)
            {
                return new MacroPlanBuildResult(
                    Array.Empty<MacroPlanEntry>(),
                    CombineFailureReasons(floorSourceFailure, fallbackFailure));
            }
        }

        double offsetSeconds = macroOffsetMs / 1000.0;
        List<MacroPlanEntry> entries = new();
        HashSet<int> midspinSeqIds = new();
        int detectedMidspinCount = 0;

        foreach (object floor in floors)
        {
            bool isMidspin = IsMidspinFloor(floor, out int midspinSeqId);
            if (isMidspin)
            {
                detectedMidspinCount++;
                if (midspinSeqId > 0)
                {
                    midspinSeqIds.Add(midspinSeqId);
                }

                if (logPreview)
                {
                    log($"MacroPlan detected midspin seqID={midspinSeqId}");
                }
            }

            if (TryBuildEntry(floor, offsetSeconds, isMidspin, out MacroPlanEntry? entry) && entry != null)
            {
                entries.Add(entry);
            }
        }

        MacroPlanEntry[] orderedEntries = entries
            .OrderBy(entry => entry.TargetTimeSeconds)
            .ThenBy(entry => entry.SeqId)
            .ToArray();
        List<MacroPlanEntry> filteredEntries = new();
        int skippedDuplicateTimeCount = 0;
        MacroPlanEntry? previous = null;
        foreach (MacroPlanEntry entry in orderedEntries)
        {
            if (previous != null &&
                !entry.IsMidspin &&
                !previous.IsMidspin &&
                entry.SeqId == previous.SeqId &&
                Math.Abs(entry.TargetTimeSeconds - previous.TargetTimeSeconds) < DuplicateTargetTimeThresholdSeconds)
            {
                skippedDuplicateTimeCount++;
                if (logPreview)
                {
                    log($"MacroPlan skipped duplicate same-floor targetTime seqID={entry.SeqId} targetTime={entry.TargetTimeSeconds:F6}s");
                }

                continue;
            }

            bool isNearMidspin = entry.IsMidspin ||
                                 midspinSeqIds.Contains(entry.SeqId - 1) ||
                                 midspinSeqIds.Contains(entry.SeqId + 1);
            MacroPlanEntry filteredEntry = isNearMidspin != entry.IsNearMidspin
                ? new MacroPlanEntry(entry.SeqId, entry.TargetTimeSeconds, entry.IsMidspin, isNearMidspin)
                : entry;
            filteredEntries.Add(filteredEntry);
            previous = filteredEntry;
        }

        MacroPlanEntry[] plan = MarkSpeedChanges(filteredEntries);

        if (plan.Length == 0)
        {
            return new MacroPlanBuildResult(
                plan,
                "Macro plan is empty after filtering seqID <= 0 and targetTime <= 0.",
                detectedMidspinCount,
                skippedDuplicateTimeCount);
        }

        if (logPreview)
        {
            foreach (MacroPlanEntry entry in plan.Take(5))
            {
                log($"MacroPlan preview seqID={entry.SeqId} targetTime={entry.TargetTimeSeconds:F6}s");
            }
        }

        return new MacroPlanBuildResult(plan, failureReason: null, detectedMidspinCount, skippedDuplicateTimeCount);
    }

    private static MacroPlanEntry[] MarkSpeedChanges(IReadOnlyList<MacroPlanEntry> entries)
    {
        if (entries.Count == 0)
        {
            return Array.Empty<MacroPlanEntry>();
        }

        bool[] nearSpeedChange = new bool[entries.Count];
        for (int i = 1; i < entries.Count - 1; i++)
        {
            double prevInterval = entries[i].TargetTimeSeconds - entries[i - 1].TargetTimeSeconds;
            double nextInterval = entries[i + 1].TargetTimeSeconds - entries[i].TargetTimeSeconds;
            if (prevInterval <= 0.0 || nextInterval <= 0.0)
            {
                continue;
            }

            double ratio = nextInterval / prevInterval;
            if (ratio > SpeedChangeRatioThreshold ||
                ratio < 1.0 / SpeedChangeRatioThreshold)
            {
                nearSpeedChange[i - 1] = true;
                nearSpeedChange[i] = true;
                nearSpeedChange[i + 1] = true;
            }
        }

        MacroPlanEntry[] marked = new MacroPlanEntry[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            double intervalSeconds = GetRepresentativeIntervalSeconds(entries, i);
            SpeedBand speedBand = ClassifySpeedBand(intervalSeconds * 1000.0);
            MacroPlanEntry entry = entries[i];
            marked[i] = new MacroPlanEntry(
                entry.SeqId,
                entry.TargetTimeSeconds,
                entry.IsMidspin,
                entry.IsNearMidspin,
                nearSpeedChange[i],
                speedBand);
        }

        return marked;
    }

    private static double GetRepresentativeIntervalSeconds(IReadOnlyList<MacroPlanEntry> entries, int index)
    {
        if (entries.Count <= 1)
        {
            return double.PositiveInfinity;
        }

        if (index > 0)
        {
            double previousInterval = entries[index].TargetTimeSeconds - entries[index - 1].TargetTimeSeconds;
            if (previousInterval > 0.0)
            {
                return previousInterval;
            }
        }

        double nextInterval = entries[index + 1].TargetTimeSeconds - entries[index].TargetTimeSeconds;
        return nextInterval > 0.0 ? nextInterval : double.PositiveInfinity;
    }

    private static SpeedBand ClassifySpeedBand(double intervalMs)
    {
        if (intervalMs < UltraFastIntervalMs)
        {
            return SpeedBand.UltraFast;
        }

        return intervalMs < FastIntervalMs
            ? SpeedBand.Fast
            : SpeedBand.Normal;
    }

    private static string CombineFailureReasons(params string?[] reasons)
    {
        string[] presentReasons = reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Cast<string>()
            .ToArray();

        return presentReasons.Length == 0
            ? "No floor source was available."
            : string.Join("; ", presentReasons);
    }

    private static IReadOnlyList<object> GetFloorsFromLevelMaker(out string? failureReason)
    {
        failureReason = null;

        object? levelMaker = ReflectionCache.GetSingletonInstance("scrLevelMaker");
        if (levelMaker == null)
        {
            failureReason = "scrLevelMaker.instance was not found.";
            return Array.Empty<object>();
        }

        object? listFloors = ReflectionCache.ReadMember(levelMaker, "listFloors", "floors");
        if (listFloors == null)
        {
            failureReason = "scrLevelMaker.instance.listFloors was not found.";
            return Array.Empty<object>();
        }

        return ReflectionCache.AsEnumerable(listFloors).Cast<object>().ToArray();
    }

    private static IReadOnlyList<object> GetFloorsFromScene(out string? failureReason)
    {
        failureReason = null;

        Type? floorType = ReflectionCache.FindType("scrFloor");
        if (floorType == null)
        {
            failureReason = "scrFloor type was not found for fallback.";
            return Array.Empty<object>();
        }

        try
        {
            return UnityEngine.Object
                .FindObjectsOfType(floorType)
                .Where(floor => floor != null)
                .Cast<object>()
                .OrderBy(floor => ReflectionCache.TryReadInt(floor, out int seqId, "seqID", "seqId", "floorSeqID") ? seqId : int.MaxValue)
                .ToArray();
        }
        catch (Exception ex)
        {
            failureReason = $"scrFloor fallback failed: {ex.GetType().Name}.";
            return Array.Empty<object>();
        }
    }

    private static bool TryBuildEntry(object floor, double offsetSeconds, bool isMidspin, out MacroPlanEntry? entry)
    {
        entry = null;
        if (!ReflectionCache.TryReadInt(floor, out int seqId, "seqID", "seqId", "floorSeqID") ||
            seqId <= 0)
        {
            return false;
        }

        object? rawTime = ReflectionCache.ReadMember(
            floor,
            "entryTimePitchAdj",
            "entryTime",
            "entryTimeSeconds");

        if (rawTime == null)
        {
            return false;
        }

        try
        {
            double targetTimeSeconds = Convert.ToDouble(rawTime) + offsetSeconds;
            if (targetTimeSeconds <= 0.0)
            {
                return false;
            }

            entry = new MacroPlanEntry(seqId, targetTimeSeconds, isMidspin);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMidspinFloor(object floor, out int seqId)
    {
        ReflectionCache.TryReadInt(floor, out seqId, "seqID", "seqId", "floorSeqID");
        if (ReflectionCache.TryReadBool(
                floor,
                out bool boolMidspin,
                "midspin",
                "midSpin",
                "isMidspin",
                "isMidSpin",
                "isMidspinFloor",
                "isMidSpinFloor") &&
            boolMidspin)
        {
            return true;
        }

        object? rawMidspin = ReflectionCache.ReadMember(floor, "midspinType", "midSpinType", "midspinState", "midSpinState");
        if (rawMidspin != null &&
            rawMidspin.ToString()?.IndexOf("mid", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        foreach (string angleName in new[] { "angle", "floorAngle", "entryAngle", "targetAngle", "targetExitAngle" })
        {
            object? rawAngle = ReflectionCache.ReadMember(floor, angleName);
            if (rawAngle == null)
            {
                continue;
            }

            try
            {
                if (Math.Abs(Convert.ToDouble(rawAngle) - 999.0) < 0.001)
                {
                    return true;
                }
            }
            catch
            {
                continue;
            }
        }

        return false;
    }
}
