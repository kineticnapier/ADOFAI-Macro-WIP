using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Macro_Inserter;

internal static class ChartFileInputPlanBuilder
{
    private const int SuspiciousChordWarningThreshold = 32;

    public static bool TryBuild(
        InternalMacroSettings settings,
        Action<string> log,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        out IReadOnlyList<InputPlanEntry> inputPlan)
    {
        inputPlan = Array.Empty<InputPlanEntry>();
        if (!TryResolveCurrentChartPath(log, out string chartPath))
        {
            log("Chart file input plan skipped: current .adofai path was not found.");
            return false;
        }

        try
        {
            IReadOnlyList<ChartFileNote> allNotes = AdofaiChartFileParser.ParseNotes(chartPath, settings.MacroOffsetMs);
            List<ChartFileNote> playableNotes = allNotes
                .Where(note => !note.IsAutoTile)
                .OrderBy(note => note.TimeSeconds)
                .ThenBy(note => note.SeqId)
                .ToList();
            int autoSkippedCount = allNotes.Count - playableNotes.Count;
            if (playableNotes.Count == 0)
            {
                log($"Chart file input plan skipped: parsed chart has no playable notes. path={chartPath}");
                return false;
            }

            inputPlan = BuildInputPlanFromNotes(settings, log, macroPlan, playableNotes, chartPath, autoSkippedCount);
            return inputPlan.Count > 0;
        }
        catch (Exception ex)
        {
            log($"Chart file input plan failed: {ex.GetType().Name}: {ex.Message} path={chartPath}");
            inputPlan = Array.Empty<InputPlanEntry>();
            return false;
        }
    }

    private static IReadOnlyList<InputPlanEntry> BuildInputPlanFromNotes(
        InternalMacroSettings settings,
        Action<string> log,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        IReadOnlyList<ChartFileNote> notes,
        string chartPath,
        int autoSkippedCount)
    {
        Dictionary<int, int> planIndexBySeqId = BuildPlanIndexBySeqId(macroPlan);
        double windowMs = Math.Max(0.0, settings.PseudoChordWindowMs);
        double exactEpsilonMs = Math.Max(0.0, settings.PseudoChordExactDuplicateEpsilonMs);
        double groupingWindowMs = Math.Max(windowMs, exactEpsilonMs);
        if (groupingWindowMs <= 0.0)
        {
            groupingWindowMs = 0.05;
        }

        List<InputPlanEntry> result = new List<InputPlanEntry>();
        int chordGroupCount = 0;
        int maxChordSize = 1;
        int index = 0;
        while (index < notes.Count)
        {
            int start = index;
            ChartFileNote first = notes[index];
            double firstTime = first.TimeSeconds;
            index++;
            while (index < notes.Count &&
                   (notes[index].TimeSeconds - firstTime) * 1000.0 <= groupingWindowMs + 0.0001)
            {
                index++;
            }

            int count = index - start;
            ChartFileNote last = notes[index - 1];
            int firstSeqId = notes.Skip(start).Take(count).Min(note => note.SeqId);
            int lastSeqId = notes.Skip(start).Take(count).Max(note => note.SeqId);
            int planStartIndex = FindPlanIndex(planIndexBySeqId, macroPlan, firstSeqId, firstTime);
            int planEndIndexExclusive = Math.Max(planStartIndex + 1, FindPlanIndex(planIndexBySeqId, macroPlan, lastSeqId, last.TimeSeconds) + 1);
            bool containsNearMidspin = false;
            List<double> hitTimes = new List<double>(count);
            List<int> expectedAfterSeqIds = new List<int>(count);
            for (int offset = 0; offset < count; offset++)
            {
                ChartFileNote note = notes[start + offset];
                containsNearMidspin |= note.IsNearMidspin;
                hitTimes.Add(note.TimeSeconds);
                expectedAfterSeqIds.Add(note.SeqId);
            }

            if (count > 1)
            {
                chordGroupCount++;
                maxChordSize = Math.Max(maxChordSize, count);
                if (count > SuspiciousChordWarningThreshold)
                {
                    log($"Chart file chord is large. keyCount={count} seqID={firstSeqId}-{lastSeqId} time={firstTime:F6}s path={Path.GetFileName(chartPath)}");
                }
            }

            result.Add(new InputPlanEntry(
                planStartIndex,
                planEndIndexExclusive,
                firstSeqId,
                lastSeqId,
                first.TimeSeconds,
                last.TimeSeconds,
                rawEntryCount: count,
                emittedHitCount: count,
                isExactDuplicateGroup: count > 1 && Math.Abs(last.TimeSeconds - first.TimeSeconds) * 1000.0 <= exactEpsilonMs,
                containsMidspin: containsNearMidspin,
                isNearMidspin: containsNearMidspin,
                isCompressed: false,
                hitTargetTimeSeconds: hitTimes,
                expectedAfterSeqIds: expectedAfterSeqIds,
                isChartFileChord: count > 1));
        }

        log($"Chart file input plan built. path={chartPath} playableNotes={notes.Count} inputEntries={result.Count} chordGroups={chordGroupCount} maxChordSize={maxChordSize} groupingWindowMs={groupingWindowMs:F3} autoSkipped={autoSkippedCount}");
        return result;
    }

    private static Dictionary<int, int> BuildPlanIndexBySeqId(IReadOnlyList<MacroPlanEntry> macroPlan)
    {
        Dictionary<int, int> result = new Dictionary<int, int>();
        for (int i = 0; i < macroPlan.Count; i++)
        {
            if (!result.ContainsKey(macroPlan[i].SeqId))
            {
                result[macroPlan[i].SeqId] = i;
            }
        }

        return result;
    }

    private static int FindPlanIndex(
        IReadOnlyDictionary<int, int> planIndexBySeqId,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        int seqId,
        double targetTimeSeconds)
    {
        if (planIndexBySeqId.TryGetValue(seqId, out int exactIndex))
        {
            return exactIndex;
        }

        if (macroPlan.Count == 0)
        {
            return 0;
        }

        int bestIndex = 0;
        double bestScore = double.MaxValue;
        for (int i = 0; i < macroPlan.Count; i++)
        {
            double seqPenalty = Math.Abs(macroPlan[i].SeqId - seqId) * 0.010;
            double timePenalty = Math.Abs(macroPlan[i].TargetTimeSeconds - targetTimeSeconds);
            double score = seqPenalty + timePenalty;
            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static bool TryResolveCurrentChartPath(Action<string> log, out string path)
    {
        List<string> candidates = new List<string>();

        // The reliable runtime path is exposed by the game as ADOBase.levelPath,
        // which delegates to scnGame.instance.levelPath. Read this first before
        // falling back to broad string scans; otherwise the process working
        // directory can be mistaken for the current chart.
        AddStaticKnownPathMembers("ADOBase", candidates, "levelPath");
        AddSingletonKnownPathMembers("scnGame", candidates, "levelPath");
        AddGcsCustomLevelPathCandidates(candidates);
        AddEditorCustomLevelPathCandidates(candidates);

        foreach (string typeName in new[]
                 {
                     "scnGame",
                     "scrLevelMaker",
                     "scrController",
                     "scnEditor",
                     "scrConductor",
                     "ADOLevel",
                     "CustomLevel"
                 })
        {
            object? instance = ReflectionCache.GetSingletonInstance(typeName);
            if (instance != null)
            {
                AddKnownPathMembers(instance, candidates);
                AddNestedKnownPathMembers(instance, candidates, "customLevel", "levelData", "level", "loadedLevel");
                AddStringMembersContainingAdofai(instance, candidates);
            }
        }

        // Lowest-priority fallback only. This must never win over ADOBase/scnGame.
        AddWorkingDirectoryCandidates(candidates);

        foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryNormalizeChartPath(candidate, out string normalized))
            {
                path = normalized;
                log($"Chart path resolved. path={normalized}");
                return true;
            }
        }

        path = string.Empty;
        if (candidates.Count > 0)
        {
            log($"Chart path candidates did not resolve to an existing .adofai file. candidates={string.Join(" | ", candidates.Take(16).ToArray())}");
        }
        else
        {
            log("Chart path candidates were empty.");
        }

        return false;
    }

    private static void AddStaticKnownPathMembers(string typeName, List<string> candidates, params string[] names)
    {
        Type? type = ReflectionCache.FindType(typeName);
        if (type == null)
        {
            return;
        }

        object? raw = ReflectionCache.ReadMember(type, instance: null, names: names);
        AddPathCandidate(raw, candidates);
    }

    private static void AddSingletonKnownPathMembers(string typeName, List<string> candidates, params string[] names)
    {
        object? instance = ReflectionCache.GetSingletonInstance(typeName);
        if (instance == null)
        {
            return;
        }

        object? raw = ReflectionCache.ReadMember(instance, names);
        AddPathCandidate(raw, candidates);
    }

    private static void AddGcsCustomLevelPathCandidates(List<string> candidates)
    {
        Type? gcsType = ReflectionCache.FindType("GCS");
        if (gcsType == null)
        {
            return;
        }

        object? rawPaths = ReflectionCache.ReadMember(gcsType, instance: null, "customLevelPaths", "CustomLevelPaths");
        int customLevelIndex = 0;
        object? rawIndex = ReflectionCache.ReadMember(gcsType, instance: null, "customLevelIndex", "CustomLevelIndex");
        if (rawIndex != null)
        {
            try
            {
                customLevelIndex = Convert.ToInt32(rawIndex);
            }
            catch
            {
                customLevelIndex = 0;
            }
        }

        if (rawPaths is string[] stringArray)
        {
            if (customLevelIndex >= 0 && customLevelIndex < stringArray.Length)
            {
                AddPathCandidate(stringArray[customLevelIndex], candidates);
            }

            foreach (string value in stringArray)
            {
                AddPathCandidate(value, candidates);
            }

            return;
        }

        if (rawPaths is IEnumerable enumerable)
        {
            int index = 0;
            List<string> values = new List<string>();
            foreach (object? item in enumerable)
            {
                if (item is string value)
                {
                    values.Add(value);
                }

                index++;
            }

            if (customLevelIndex >= 0 && customLevelIndex < values.Count)
            {
                AddPathCandidate(values[customLevelIndex], candidates);
            }

            foreach (string value in values)
            {
                AddPathCandidate(value, candidates);
            }
        }
    }

    private static void AddEditorCustomLevelPathCandidates(List<string> candidates)
    {
        object? editor = ReflectionCache.GetSingletonInstance("scnEditor");
        if (editor == null)
        {
            return;
        }

        object? customLevel = ReflectionCache.ReadMember(editor, "customLevel", "level", "levelData");
        if (customLevel != null)
        {
            AddKnownPathMembers(customLevel, candidates);
            AddStringMembersContainingAdofai(customLevel, candidates);
        }
    }

    private static void AddNestedKnownPathMembers(object instance, List<string> candidates, params string[] memberNames)
    {
        foreach (string memberName in memberNames)
        {
            object? nested = ReflectionCache.ReadMember(instance, memberName);
            if (nested == null)
            {
                continue;
            }

            AddKnownPathMembers(nested, candidates);
            AddStringMembersContainingAdofai(nested, candidates);
        }
    }

    private static void AddPathCandidate(object? raw, List<string> candidates)
    {
        if (raw == null)
        {
            return;
        }

        if (raw is string value)
        {
            candidates.Add(value);
            return;
        }

        if (raw is IEnumerable enumerable && raw is not string)
        {
            foreach (object? item in enumerable)
            {
                if (item is string stringValue)
                {
                    candidates.Add(stringValue);
                }
            }

            return;
        }

        candidates.Add(raw.ToString() ?? string.Empty);
    }

    private static void AddKnownPathMembers(object instance, List<string> candidates)
    {
        object? raw = ReflectionCache.ReadMember(
            instance,
            "levelPath",
            "levelFile",
            "levelFilePath",
            "loadedLevelPath",
            "currentLevelPath",
            "currentLevelFile",
            "currentFilePath",
            "filePath",
            "path",
            "fullPath",
            "songFilename",
            "levelFilename");
        AddPathCandidate(raw, candidates);
    }

    private static void AddStringMembersContainingAdofai(object instance, List<string> candidates)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = instance.GetType();
        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (field.FieldType != typeof(string))
            {
                continue;
            }

            try
            {
                if (field.GetValue(instance) is string value && LooksLikePathCandidate(value))
                {
                    candidates.Add(value);
                }
            }
            catch
            {
                continue;
            }
        }

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (property.PropertyType != typeof(string) || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            MethodInfo? getter = property.GetGetMethod(nonPublic: true);
            if (getter == null)
            {
                continue;
            }

            try
            {
                if (property.GetValue(instance, null) is string value && LooksLikePathCandidate(value))
                {
                    candidates.Add(value);
                }
            }
            catch
            {
                continue;
            }
        }
    }

    private static bool LooksLikePathCandidate(string value)
    {
        return value.IndexOf(".adofai", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("CustomLevels", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("Levels", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddWorkingDirectoryCandidates(List<string> candidates)
    {
        try
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            if (Directory.Exists(currentDirectory))
            {
                candidates.Add(currentDirectory);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool TryNormalizeChartPath(string rawCandidate, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(rawCandidate))
        {
            return false;
        }

        string candidate = rawCandidate.Trim().Trim('"');
        candidate = candidate.Replace('/', Path.DirectorySeparatorChar);

        if (File.Exists(candidate) &&
            string.Equals(Path.GetExtension(candidate), ".adofai", StringComparison.OrdinalIgnoreCase))
        {
            normalized = Path.GetFullPath(candidate);
            return true;
        }

        if (Directory.Exists(candidate))
        {
            string? file = Directory.GetFiles(candidate, "*.adofai", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (file != null)
            {
                normalized = Path.GetFullPath(file);
                return true;
            }
        }

        int adofaiIndex = candidate.IndexOf(".adofai", StringComparison.OrdinalIgnoreCase);
        if (adofaiIndex >= 0)
        {
            string sliced = candidate.Substring(0, adofaiIndex + ".adofai".Length);
            if (File.Exists(sliced))
            {
                normalized = Path.GetFullPath(sliced);
                return true;
            }
        }

        return false;
    }
}
