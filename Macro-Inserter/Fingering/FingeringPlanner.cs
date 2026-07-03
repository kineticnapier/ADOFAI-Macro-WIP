using System;
using System.Collections.Generic;
using System.Linq;

namespace Macro_Inserter;

internal static class FingeringPlanner
{
    private const int BankSize = 4;
    private const double InitialMaxVisualBpm = 1000.0;
    private const double MaxExpandedVisualBpm = 8000.0;
    private const double BpmChangeEpsilon = 0.0001;

    private static readonly string[] FallbackKeys =
    {
        "Tab", "1", "2", "E",
        "LShift", "LCtrl", "C", "無変換",
        "P", "^", "\\", "Enter",
        "K", ".", "RShift", "RCtrl"
    };

    public static IReadOnlyList<InputPlanEntry> ApplyBeatBankFingering(
        IReadOnlyList<InputPlanEntry> inputPlan,
        string macroKeyViewerKeysText,
        IReadOnlyDictionary<int, double> bpmBySeqId,
        Action<string> log)
    {
        if (inputPlan.Count == 0)
        {
            return inputPlan;
        }

        string[] configuredKeys = MacroKeyViewerState.ParseKeyNames(macroKeyViewerKeysText);
        string[] keys = configuredKeys.Length > 0 ? configuredKeys : FallbackKeys;
        IReadOnlyList<IReadOnlyList<string>> banks = BuildBanks(keys);
        if (banks.Count == 0)
        {
            log("Natural fingering skipped: no MacroKeyViewer keys are configured.");
            return inputPlan;
        }

        InputPlanEntry[] assigned = inputPlan.ToArray();
        int sectionCount = 0;
        int expandedSectionCount = 0;
        int maxBucketInputs = 0;
        double previousRawBpm = ResolveBpm(inputPlan[0], bpmBySeqId, fallbackBpm: 120.0);
        double previousVisualBpm = NormalizeVisualBpm(previousRawBpm);
        int sectionStart = 0;

        for (int i = 1; i <= inputPlan.Count; i++)
        {
            bool endSection = i >= inputPlan.Count;
            double visualBpm = previousVisualBpm;
            if (!endSection)
            {
                double rawBpm = ResolveBpm(inputPlan[i], bpmBySeqId, previousRawBpm);
                visualBpm = NormalizeVisualBpm(rawBpm);
                endSection = Math.Abs(visualBpm - previousVisualBpm) > BpmChangeEpsilon;
                if (!endSection)
                {
                    previousRawBpm = rawBpm;
                    previousVisualBpm = visualBpm;
                }
            }

            if (endSection)
            {
                SectionStats stats = AssignSection(
                    inputPlan,
                    assigned,
                    sectionStart,
                    i,
                    previousVisualBpm,
                    banks,
                    keys.Length);

                sectionCount++;
                if (stats.Expanded)
                {
                    expandedSectionCount++;
                }

                maxBucketInputs = Math.Max(maxBucketInputs, stats.MaxBucketInputs);

                sectionStart = i;
                if (i < inputPlan.Count)
                {
                    previousRawBpm = ResolveBpm(inputPlan[i], bpmBySeqId, previousRawBpm);
                    previousVisualBpm = NormalizeVisualBpm(previousRawBpm);
                }
            }
        }

        log(
            $"Natural fingering v30 beat-bank plan built. entries={assigned.Length} keys={keys.Length} banks={banks.Count} bankSize={BankSize} sections={sectionCount} expandedSections={expandedSectionCount} maxBucketInputs={maxBucketInputs} bpmMapEntries={bpmBySeqId.Count}");
        return assigned;
    }

    private static SectionStats AssignSection(
        IReadOnlyList<InputPlanEntry> source,
        InputPlanEntry[] assigned,
        int startIndex,
        int endIndexExclusive,
        double initialVisualBpm,
        IReadOnlyList<IReadOnlyList<string>> banks,
        int totalKeyCount)
    {
        if (startIndex >= endIndexExclusive)
        {
            return new SectionStats(false, 0);
        }

        double visualBpm = Math.Max(1.0, initialVisualBpm);
        double sectionStartTime = source[startIndex].FirstTargetTimeSeconds;
        SortedDictionary<long, BucketState> buckets = BuildBuckets(source, startIndex, endIndexExclusive, sectionStartTime, visualBpm);
        int maxBucketInputs = buckets.Count == 0 ? 0 : buckets.Values.Max(bucket => bucket.TotalInputs);
        bool expanded = false;

        while (maxBucketInputs > totalKeyCount && visualBpm < MaxExpandedVisualBpm)
        {
            visualBpm = Math.Min(MaxExpandedVisualBpm, visualBpm * 2.0);
            buckets = BuildBuckets(source, startIndex, endIndexExclusive, sectionStartTime, visualBpm);
            maxBucketInputs = buckets.Count == 0 ? 0 : buckets.Values.Max(bucket => bucket.TotalInputs);
            expanded = true;
        }

        foreach (KeyValuePair<long, BucketState> pair in buckets)
        {
            BucketState bucket = pair.Value;
            string[] keyOrder = BuildBucketKeyOrder(pair.Key, banks);
            int cursor = 0;
            foreach (int entryIndex in bucket.EntryIndices)
            {
                InputPlanEntry entry = source[entryIndex];
                int count = Math.Max(1, entry.EmittedHitCount);
                string[] entryKeys = new string[count];
                for (int i = 0; i < count; i++)
                {
                    entryKeys[i] = keyOrder[cursor % keyOrder.Length];
                    cursor++;
                }

                assigned[entryIndex] = CloneWithAssignedKeys(entry, entryKeys);
            }
        }

        return new SectionStats(expanded, maxBucketInputs);
    }

    private static SortedDictionary<long, BucketState> BuildBuckets(
        IReadOnlyList<InputPlanEntry> inputPlan,
        int startIndex,
        int endIndexExclusive,
        double sectionStartTime,
        double visualBpm)
    {
        SortedDictionary<long, BucketState> buckets = new SortedDictionary<long, BucketState>();
        double beatSeconds = 60.0 / Math.Max(1.0, visualBpm);
        for (int i = startIndex; i < endIndexExclusive; i++)
        {
            InputPlanEntry entry = inputPlan[i];
            double rawBucket = (entry.FirstTargetTimeSeconds - sectionStartTime) / beatSeconds;
            long bucketIndex = Math.Max(0, (long)Math.Floor(rawBucket + 0.0000001));
            if (!buckets.TryGetValue(bucketIndex, out BucketState? bucket))
            {
                bucket = new BucketState();
                buckets[bucketIndex] = bucket;
            }

            bucket.EntryIndices.Add(i);
            bucket.TotalInputs += Math.Max(1, entry.EmittedHitCount);
        }

        return buckets;
    }

    private static string[] BuildBucketKeyOrder(long bucketIndex, IReadOnlyList<IReadOnlyList<string>> banks)
    {
        int bankCount = banks.Count;
        int primary = (int)(PositiveModulo(bucketIndex, bankCount));
        List<string> keys = new List<string>();
        for (int offset = 0; offset < bankCount; offset++)
        {
            IReadOnlyList<string> bank = banks[(primary + offset) % bankCount];
            for (int i = 0; i < bank.Count; i++)
            {
                keys.Add(bank[i]);
            }
        }

        return keys.Count > 0 ? keys.ToArray() : FallbackKeys;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildBanks(IReadOnlyList<string> keys)
    {
        List<IReadOnlyList<string>> banks = new List<IReadOnlyList<string>>();
        for (int i = 0; i < keys.Count; i += BankSize)
        {
            string[] bank = keys.Skip(i).Take(BankSize).ToArray();
            if (bank.Length > 0)
            {
                banks.Add(bank);
            }
        }

        return banks;
    }

    private static double ResolveBpm(InputPlanEntry entry, IReadOnlyDictionary<int, double> bpmBySeqId, double fallbackBpm)
    {
        if (bpmBySeqId.TryGetValue(entry.FirstSeqId, out double bpm) && bpm > 0.0)
        {
            return bpm;
        }

        if (bpmBySeqId.TryGetValue(entry.LastSeqId, out bpm) && bpm > 0.0)
        {
            return bpm;
        }

        return fallbackBpm > 0.0 ? fallbackBpm : 120.0;
    }

    private static double NormalizeVisualBpm(double bpm)
    {
        double result = bpm > 0.0 ? bpm : 120.0;
        while (result > InitialMaxVisualBpm)
        {
            result *= 0.5;
        }

        return Math.Max(1.0, result);
    }

    private static long PositiveModulo(long value, int modulus)
    {
        if (modulus <= 0)
        {
            return 0;
        }

        long result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static InputPlanEntry CloneWithAssignedKeys(InputPlanEntry entry, IReadOnlyList<string> assignedKeyNames)
    {
        return new InputPlanEntry(
            entry.PlanStartIndex,
            entry.PlanEndIndexExclusive,
            entry.FirstSeqId,
            entry.LastSeqId,
            entry.FirstTargetTimeSeconds,
            entry.LastTargetTimeSeconds,
            entry.RawEntryCount,
            entry.EmittedHitCount,
            entry.IsExactDuplicateGroup,
            entry.ContainsMidspin,
            entry.IsNearMidspin,
            entry.IsCompressed,
            entry.HitTargetTimeSeconds,
            entry.ExpectedAfterSeqIds,
            entry.IsChartFileChord,
            entry.UseInputPatchPipeline,
            assignedKeyNames);
    }

    private sealed class BucketState
    {
        public List<int> EntryIndices { get; } = new List<int>();
        public int TotalInputs { get; set; }
    }

    private readonly struct SectionStats
    {
        public SectionStats(bool expanded, int maxBucketInputs)
        {
            Expanded = expanded;
            MaxBucketInputs = maxBucketInputs;
        }

        public bool Expanded { get; }
        public int MaxBucketInputs { get; }
    }
}
