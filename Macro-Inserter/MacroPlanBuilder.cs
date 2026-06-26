using System;
using System.Collections.Generic;
using System.Linq;

namespace Macro_Inserter;

internal sealed class MacroPlanBuilder
{
    private readonly Action<string> log;

    public MacroPlanBuilder(Action<string> log)
    {
        this.log = log;
    }

    public IReadOnlyList<MacroPlanEntry> Build(double macroOffsetMs)
    {
        object? levelMaker = ReflectionCache.GetSingletonInstance("scrLevelMaker");
        if (levelMaker == null)
        {
            log("scrLevelMaker.instance was not found.");
            return Array.Empty<MacroPlanEntry>();
        }

        object? listFloors = ReflectionCache.ReadMember(levelMaker, "listFloors", "floors");
        if (listFloors == null)
        {
            log("scrLevelMaker.instance.listFloors was not found.");
            return Array.Empty<MacroPlanEntry>();
        }

        double offsetSeconds = macroOffsetMs / 1000.0;
        List<MacroPlanEntry> entries = new();

        foreach (object floor in ReflectionCache.AsEnumerable(listFloors))
        {
            if (!ReflectionCache.TryReadInt(floor, out int seqId, "seqID", "seqId", "floorSeqID"))
            {
                continue;
            }

            object? rawTime = ReflectionCache.ReadMember(
                floor,
                "entryTimePitchAdj",
                "entryTime",
                "entryTimeSeconds");

            if (rawTime == null)
            {
                continue;
            }

            try
            {
                double entryTimeSeconds = Convert.ToDouble(rawTime);
                entries.Add(new MacroPlanEntry(seqId, entryTimeSeconds + offsetSeconds));
            }
            catch
            {
                log($"Could not parse entryTimePitchAdj for seqID {seqId}.");
            }
        }

        return entries
            .OrderBy(entry => entry.TargetTimeSeconds)
            .ThenBy(entry => entry.SeqId)
            .ToArray();
    }
}
