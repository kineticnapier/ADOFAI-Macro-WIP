using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class MacroPlanBuilder
{
    private const double DuplicateTargetTimeThresholdSeconds = 0.001;

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
        HashSet<int> skippedMidspinSeqIds = new();
        int skippedMidspinCount = 0;

        foreach (object floor in floors)
        {
            if (IsMidspinFloor(floor, out int midspinSeqId))
            {
                skippedMidspinCount++;
                if (midspinSeqId > 0)
                {
                    skippedMidspinSeqIds.Add(midspinSeqId);
                }

                if (logPreview)
                {
                    log($"MacroPlan skipped midspin seqID={midspinSeqId}");
                }

                continue;
            }

            if (TryBuildEntry(floor, offsetSeconds, out MacroPlanEntry? entry) && entry != null)
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
                Math.Abs(entry.TargetTimeSeconds - previous.TargetTimeSeconds) < DuplicateTargetTimeThresholdSeconds)
            {
                skippedDuplicateTimeCount++;
                if (logPreview)
                {
                    log($"MacroPlan skipped duplicate targetTime seqID={entry.SeqId} previousSeqID={previous.SeqId} targetTime={entry.TargetTimeSeconds:F6}s");
                }

                continue;
            }

            bool isNearMidspin = skippedMidspinSeqIds.Contains(entry.SeqId - 1) ||
                                 skippedMidspinSeqIds.Contains(entry.SeqId + 1);
            MacroPlanEntry filteredEntry = isNearMidspin
                ? new MacroPlanEntry(entry.SeqId, entry.TargetTimeSeconds, isNearMidspin: true)
                : entry;
            filteredEntries.Add(filteredEntry);
            previous = filteredEntry;
        }

        MacroPlanEntry[] plan = filteredEntries.ToArray();

        if (plan.Length == 0)
        {
            return new MacroPlanBuildResult(
                plan,
                "Macro plan is empty after filtering seqID <= 0 and targetTime <= 0.",
                skippedMidspinCount,
                skippedDuplicateTimeCount);
        }

        if (logPreview)
        {
            foreach (MacroPlanEntry entry in plan.Take(5))
            {
                log($"MacroPlan preview seqID={entry.SeqId} targetTime={entry.TargetTimeSeconds:F6}s");
            }
        }

        return new MacroPlanBuildResult(plan, failureReason: null, skippedMidspinCount, skippedDuplicateTimeCount);
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

    private static bool TryBuildEntry(object floor, double offsetSeconds, out MacroPlanEntry? entry)
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

            entry = new MacroPlanEntry(seqId, targetTimeSeconds);
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
