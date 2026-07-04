using System;
using System.Collections.Generic;
using System.Linq;

namespace Macro_Inserter;

internal static class FingeringPlanner
{
    private const int BankSize = 4;
    private const double MaxFoldedVisualBpm = 1000.0;
    private const double MaxRaisedVisualBpm = 500.0;
    private const double MaxExpandedVisualBpm = 8000.0;
    private const double BpmChangeEpsilon = 0.0001;

    private static readonly string[] FallbackKeys =
    {
        "Tab", "1", "2", "E",
        "P", "^", "\\", "Enter",
        "LShift", "LCtrl", "C", "NC",
        "K", ".", "RCtrl", "RShift",
        "F1", "F2", "F3", "F4",
        "F5", "F6", "F7", "F8"
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
            $"Natural fingering v35 beat-bank plan built. entries={assigned.Length} keys={keys.Length} banks={banks.Count} bankSize={BankSize} sections={sectionCount} expandedSections={expandedSectionCount} maxBucketInputs={maxBucketInputs} bpmMapEntries={bpmBySeqId.Count}");
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
        // v35: do not rotate through every body part on successive beats.
        // Each beat bucket starts from an upper bank again; lower/foot banks are
        // only appended when that bucket contains more inputs than the upper bank
        // can cover. For balance, alternate the leading side per beat bucket:
        //   even bucket: left upper -> left lower -> right upper -> right lower -> left foot -> right foot
        //   odd  bucket: right upper -> right lower -> left upper -> left lower -> right foot -> left foot
        // With <=4 inputs this means every beat uses only an upper bank, instead
        // of walking down through lower/foot banks over time.
        int bankCount = banks.Count;
        if (bankCount <= 0)
        {
            return FallbackKeys;
        }

        bool startRight = PositiveModulo(bucketIndex, 2) == 1;

        if (bankCount >= 6)
        {
            int[] order = startRight
                ? new[] { 2, 3, 0, 1, 5, 4 }
                : new[] { 0, 1, 2, 3, 4, 5 };
            return FlattenBanks(banks, order);
        }

        if (bankCount >= 4)
        {
            int[] order = startRight
                ? new[] { 2, 3, 0, 1 }
                : new[] { 0, 1, 2, 3 };
            return FlattenBanks(banks, order);
        }

        if (bankCount >= 2)
        {
            int[] order = startRight
                ? new[] { 1, 0 }
                : new[] { 0, 1 };
            return FlattenBanks(banks, order);
        }

        return FlattenBanks(banks, new[] { 0 });
    }

    private static string[] FlattenBanks(IReadOnlyList<IReadOnlyList<string>> banks, IReadOnlyList<int> order)
    {
        List<string> keys = new List<string>();
        for (int i = 0; i < order.Count; i++)
        {
            int bankIndex = order[i];
            if (bankIndex < 0 || bankIndex >= banks.Count)
            {
                continue;
            }

            IReadOnlyList<string> bank = banks[bankIndex];
            for (int j = 0; j < bank.Count; j++)
            {
                keys.Add(bank[j]);
            }
        }

        return keys.Count > 0 ? keys.ToArray() : FallbackKeys;
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildBanks(IReadOnlyList<string> keys)
    {
        // MacroKeyViewer is displayed row-major in 8 columns:
        //   0..3   = left upper,  4..7   = right upper
        //   8..11  = left lower,  12..15 = right lower
        //   16..19 = left foot,   20..23 = right foot
        // The natural fingering rotation should be logical body order, not row order:
        // left upper -> left lower -> right upper -> right lower -> left foot -> right foot.
        //
        // Within each left-side bank, use the inside-to-outside order requested for the
        // row-major display: Tab 1 2 E becomes E 2 1 Tab. Right-side banks keep their
        // displayed order: P ^ \ Enter stays P ^ \ Enter. v33/v34/v35 starts each beat from
        // an upper bank again and only appends lower/foot banks when one beat needs
        // more than the upper bank can cover.
        List<IReadOnlyList<string>> banks = new List<IReadOnlyList<string>>();
        if (keys.Count >= 16)
        {
            AddBankFromRange(keys, banks, 0, reverse: true);
            AddBankFromRange(keys, banks, 8, reverse: true);
            AddBankFromRange(keys, banks, 4, reverse: false);
            AddBankFromRange(keys, banks, 12, reverse: false);
            if (keys.Count >= 24)
            {
                AddBankFromRange(keys, banks, 16, reverse: true);
                AddBankFromRange(keys, banks, 20, reverse: false);
            }

            for (int i = keys.Count >= 24 ? 24 : 16; i < keys.Count; i += BankSize)
            {
                AddBankFromRange(keys, banks, i, reverse: false);
            }

            return banks;
        }

        for (int i = 0; i < keys.Count; i += BankSize)
        {
            AddBankFromRange(keys, banks, i, reverse: false);
        }

        return banks;
    }

    private static void AddBankFromRange(IReadOnlyList<string> keys, List<IReadOnlyList<string>> banks, int startIndex, bool reverse)
    {
        if (startIndex >= keys.Count)
        {
            return;
        }

        string[] bank = keys.Skip(startIndex).Take(BankSize).ToArray();
        if (reverse)
        {
            Array.Reverse(bank);
        }

        if (bank.Length > 0)
        {
            banks.Add(bank);
        }
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

        // Downward folding is still allowed until the visual BPM is <=1000.
        // Example: 2500 -> 1250 -> 625.
        while (result > MaxFoldedVisualBpm)
        {
            result *= 0.5;
        }

        // Upward refinement is intentionally more conservative than v34.
        // Raise only while the doubled value is <=500, so 100 -> 200 -> 400,
        // but 300 stays 300. This keeps medium BPM sections from becoming too
        // twitchy while still giving very low BPM sections a useful visual grid.
        while (result * 2.0 <= MaxRaisedVisualBpm)
        {
            result *= 2.0;
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
