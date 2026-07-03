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
        if (macroPlan.Count == 0)
        {
            log("Runtime input-pipeline plan skipped: runtime MacroPlan is empty.");
            return false;
        }

        string? chartPath = null;
        if (TryResolveCurrentChartPath(log, out string resolvedPath))
        {
            chartPath = resolvedPath;
            log($"Runtime input-pipeline plan v23: chart path resolved but AutoPlayTiles hints are ignored. path={resolvedPath}");
        }
        else
        {
            log("Runtime input-pipeline plan v23: current .adofai path was not found; AutoPlayTiles hints are ignored.");
        }

        HashSet<int> autoSeqIds = new HashSet<int>();
        inputPlan = BuildInputPlanFromRuntimePlan(settings, log, macroPlan, autoSeqIds, chartPath);
        return inputPlan.Count > 0;
    }

    private static IReadOnlyList<InputPlanEntry> BuildInputPlanFromRuntimePlan(
        InternalMacroSettings settings,
        Action<string> log,
        IReadOnlyList<MacroPlanEntry> macroPlan,
        HashSet<int> autoSeqIds,
        string? chartPath)
    {
        double windowMs = Math.Max(0.0, settings.PseudoChordWindowMs);
        double exactEpsilonMs = Math.Max(0.0, settings.PseudoChordExactDuplicateEpsilonMs);
        double groupingWindowMs = Math.Max(windowMs, exactEpsilonMs);
        if (groupingWindowMs <= 0.0)
        {
            groupingWindowMs = 0.05;
        }

        List<RuntimePlanItem> items = new List<RuntimePlanItem>(macroPlan.Count);
        for (int i = 0; i < macroPlan.Count; i++)
        {
            MacroPlanEntry entry = macroPlan[i];
            if (entry.SeqId <= 0 || entry.TargetTimeSeconds <= 0.0)
            {
                continue;
            }

            items.Add(new RuntimePlanItem(i, entry, autoSeqIds.Contains(entry.SeqId)));
        }

        items = items
            .OrderBy(item => item.Entry.TargetTimeSeconds)
            .ThenBy(item => item.Entry.SeqId)
            .ToList();

        List<InputPlanEntry> result = new List<InputPlanEntry>();
        int groupCount = 0;
        int inputPatchGroupCount = 0;
        int skippedAutoCount = 0;
        int skippedMidspinOnlyGroupCount = 0;
        int maxRawGroupSize = 1;
        int maxKeyCount = 1;
        int index = 0;
        while (index < items.Count)
        {
            int start = index;
            RuntimePlanItem first = items[index];
            double firstTime = first.Entry.TargetTimeSeconds;
            index++;
            while (index < items.Count &&
                   (items[index].Entry.TargetTimeSeconds - firstTime) * 1000.0 <= groupingWindowMs + 0.0001)
            {
                index++;
            }

            int count = index - start;
            RuntimePlanItem last = items[index - 1];
            IReadOnlyList<RuntimePlanItem> group = items.GetRange(start, count);
            groupCount++;
            maxRawGroupSize = Math.Max(maxRawGroupSize, count);

            int firstSeqId = group.Min(item => item.Entry.SeqId);
            int lastSeqId = group.Max(item => item.Entry.SeqId);
            int planStartIndex = group.Min(item => item.PlanIndex);
            int planEndIndexExclusive = group.Max(item => item.PlanIndex) + 1;
            bool containsMidspin = group.Any(item => item.Entry.IsMidspin || item.Entry.IsNearMidspin);
            bool containsAuto = group.Any(item => item.IsAutoTile);
            int autoCount = group.Count(item => item.IsAutoTile);
            skippedAutoCount += autoCount;

            // Game-code invariant:
            // - Keyboard input enters through CountValidKeysPressed().
            // - HitAutoFloors adds CountValidKeysPressed() entries into keyTimes.
            // - UpdateHoldKeys consumes keyTimes and calls Hit(false).
            // - Hit(false) itself adds one extra keyTime when the landed floor is midspin.
            // Therefore external key count is runtime floors minus midspin floors.
            // v23 deliberately ignores chart-file AutoPlayTiles hints because those hints can
            // remove long runtime floor ranges from the input plan even when gameplay still
            // requires the scheduler to bridge through them.
            List<RuntimePlanItem> externalKeyItems = group
                .Where(item => !item.Entry.IsMidspin)
                .OrderBy(item => item.Entry.TargetTimeSeconds)
                .ThenBy(item => item.Entry.SeqId)
                .ToList();

            int keyCount = externalKeyItems.Count;
            if (keyCount <= 0)
            {
                skippedMidspinOnlyGroupCount++;
                continue;
            }

            maxKeyCount = Math.Max(maxKeyCount, keyCount);
            if (keyCount > SuspiciousChordWarningThreshold)
            {
                log($"Runtime input-pipeline group is large. keyCount={keyCount} rawEntryCount={count} seqID={firstSeqId}-{lastSeqId} time={firstTime:F6}s");
            }

            if (keyCount > 1 || count > 1)
            {
                inputPatchGroupCount++;
            }

            List<double> hitTimes = new List<double>(keyCount);
            List<int> expectedAfterSeqIds = new List<int>(keyCount);
            foreach (RuntimePlanItem keyItem in externalKeyItems)
            {
                hitTimes.Add(keyItem.Entry.TargetTimeSeconds);
                expectedAfterSeqIds.Add(lastSeqId);
            }

            result.Add(new InputPlanEntry(
                planStartIndex,
                planEndIndexExclusive,
                firstSeqId,
                lastSeqId,
                first.Entry.TargetTimeSeconds,
                last.Entry.TargetTimeSeconds,
                rawEntryCount: count,
                emittedHitCount: keyCount,
                isExactDuplicateGroup: Math.Abs(last.Entry.TargetTimeSeconds - first.Entry.TargetTimeSeconds) * 1000.0 <= exactEpsilonMs,
                containsMidspin: containsMidspin,
                isNearMidspin: containsMidspin,
                isCompressed: keyCount < count,
                hitTargetTimeSeconds: hitTimes,
                expectedAfterSeqIds: expectedAfterSeqIds,
                isChartFileChord: false,
                useInputPatchPipeline: true));
        }

        string chartText = string.IsNullOrEmpty(chartPath) ? "<none>" : chartPath;
        log(
            $"Runtime input-pipeline plan built. runtimeEntries={items.Count} inputEntries={result.Count} rawGroups={groupCount} inputPatchGroups={inputPatchGroupCount} maxRawGroupSize={maxRawGroupSize} maxKeyCount={maxKeyCount} groupingWindowMs={groupingWindowMs:F3} autoSeqIdsIgnored={autoSeqIds.Count} skippedAutoMembers={skippedAutoCount} skippedNoExternalKeyGroups={skippedMidspinOnlyGroupCount} chart={chartText}");
        return result;
    }

    private static HashSet<int> TryReadAutoSeqIdsFromCurrentChart(Action<string> log, out string? chartPath)
    {
        chartPath = null;
        HashSet<int> result = new HashSet<int>();
        if (!TryResolveCurrentChartPath(log, out string resolvedPath))
        {
            log("Runtime input-pipeline plan: current .adofai path was not found; AutoPlayTiles filtering is disabled for this run.");
            return result;
        }

        chartPath = resolvedPath;
        try
        {
            IReadOnlyList<ChartFileNote> notes = AdofaiChartFileParser.ParseNotes(resolvedPath, macroOffsetMs: 0.0);
            foreach (ChartFileNote note in notes)
            {
                if (note.IsAutoTile)
                {
                    result.Add(note.SeqId);
                }
            }

            log($"Runtime input-pipeline plan: loaded AutoPlayTiles hints from chart. path={resolvedPath} autoSeqIds={result.Count}");
            return result;
        }
        catch (Exception ex)
        {
            log($"Runtime input-pipeline plan: failed to read AutoPlayTiles hints; continuing without chart trust. error={ex.GetType().Name}: {ex.Message} path={resolvedPath}");
            result.Clear();
            return result;
        }
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
            List<string> values = new List<string>();
            foreach (object? item in enumerable)
            {
                if (item is string value)
                {
                    values.Add(value);
                }
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

    private sealed class RuntimePlanItem
    {
        public RuntimePlanItem(int planIndex, MacroPlanEntry entry, bool isAutoTile)
        {
            PlanIndex = planIndex;
            Entry = entry;
            IsAutoTile = isAutoTile;
        }

        public int PlanIndex { get; }

        public MacroPlanEntry Entry { get; }

        public bool IsAutoTile { get; }
    }
}
