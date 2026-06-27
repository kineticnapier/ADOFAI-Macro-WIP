using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class MacroPlanBuilder
{
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

        foreach (object floor in floors)
        {
            if (TryBuildEntry(floor, offsetSeconds, out MacroPlanEntry? entry) && entry != null)
            {
                entries.Add(entry);
            }
        }

        MacroPlanEntry[] plan = entries
            .OrderBy(entry => entry.TargetTimeSeconds)
            .ThenBy(entry => entry.SeqId)
            .ToArray();

        if (plan.Length == 0)
        {
            return new MacroPlanBuildResult(
                plan,
                "Macro plan is empty after filtering seqID <= 0 and targetTime <= 0.");
        }

        if (logPreview)
        {
            foreach (MacroPlanEntry entry in plan.Take(5))
            {
                log($"MacroPlan preview seqID={entry.SeqId} targetTime={entry.TargetTimeSeconds:F6}s");
            }
        }

        return new MacroPlanBuildResult(plan, failureReason: null);
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
}
