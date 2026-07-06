using System;
using System.Collections.Generic;
using System.Linq;

namespace Macro_Inserter;

internal static class FingeringPlanner
{
    private const int BankSize = 4;
        private const double MaxExpandedVisualBpm = 8000.0;
    private const double BpmChangeEpsilon = 0.0001;
        private const int PreviewAssignedKeys = 32;

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

        NaturalFingeringOptions.Load();
        bool enableFingeringLog = NaturalFingeringOptions.EnableFingeringLog;
        int debugLimit = enableFingeringLog
            ? NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Verbose)
                ? Math.Max(0, NaturalFingeringOptions.FingeringVerboseLogLimit)
                : NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Normal)
                    ? Math.Max(0, NaturalFingeringOptions.FingeringNormalLogLimit)
                    : 0
            : 0;

        InputPlanEntry[] assigned = inputPlan.ToArray();
        FingeringDebugCollector debug = new FingeringDebugCollector(debugLimit);
        int sectionCount = 0;
        int expandedSectionCount = 0;
        int rollingOverflowSectionCount = 0;
        int rollingOverflowBucketCount = 0;
        int maxBucketInputs = 0;
        int maxRollingKeys = 0;
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
                    previousRawBpm,
                    previousVisualBpm,
                    banks,
                    keys.Length,
                    sectionCount,
                    debug);

                sectionCount++;
                if (stats.Expanded)
                {
                    expandedSectionCount++;
                }

                if (stats.RollingOverflowBucketCount > 0)
                {
                    rollingOverflowSectionCount++;
                    rollingOverflowBucketCount += stats.RollingOverflowBucketCount;
                    maxRollingKeys = Math.Max(maxRollingKeys, stats.RollingKeyCount);
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

        if (enableFingeringLog && NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Minimal))
        {
            log(
                $"Natural fingering v66 bucket-rolling-overflow plan built. entries={assigned.Length} keys={keys.Length} banks={banks.Count} bankSize={BankSize} sections={sectionCount} expandedSections={expandedSectionCount} rollingOverflowSections={rollingOverflowSectionCount} rollingOverflowBuckets={rollingOverflowBucketCount} maxBucketInputs={maxBucketInputs} maxRollingKeys={maxRollingKeys} lowerBankBuckets={debug.LowerBankBucketCount} oppositeSideBuckets={debug.OppositeSideBucketCount} footBuckets={debug.FootBucketCount} wrappedBuckets={debug.WrappedBucketCount} debugEventsLogged={debug.LoggedCount} debugEventsOmitted={debug.OmittedCount} bpmMapEntries={bpmBySeqId.Count} foldDownMaxBpm={NaturalFingeringOptions.FoldDownMaxBpm:F3} raiseUpMaxBpm={NaturalFingeringOptions.RaiseUpMaxBpm:F3} rollingOverflow={NaturalFingeringOptions.EnableRollingOverflowFingering} rollingStartInputs={NaturalFingeringOptions.RollingOverflowStartInputs} rollingMaxKeys={NaturalFingeringOptions.RollingOverflowMaxKeys} rollingUseFeet={NaturalFingeringOptions.RollingOverflowUseFeet} logMode={NaturalFingeringOptions.LogMode} normalLimit={NaturalFingeringOptions.FingeringNormalLogLimit} verboseLimit={NaturalFingeringOptions.FingeringVerboseLogLimit}");
        }

        if (enableFingeringLog && NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Normal))
        {
            debug.Flush(log);
        }
        return assigned;
    }

    private static SectionStats AssignSection(
        IReadOnlyList<InputPlanEntry> source,
        InputPlanEntry[] assigned,
        int startIndex,
        int endIndexExclusive,
        double rawBpm,
        double initialVisualBpm,
        IReadOnlyList<IReadOnlyList<string>> banks,
        int totalKeyCount,
        int sectionIndex,
        FingeringDebugCollector debug)
    {
        if (startIndex >= endIndexExclusive)
        {
            return new SectionStats(false, 0, initialVisualBpm, 0, 0);
        }

        double visualBpm = Math.Max(1.0, initialVisualBpm);
        double sectionStartTime = source[startIndex].FirstTargetTimeSeconds;
        SortedDictionary<long, BucketState> buckets = BuildBuckets(source, startIndex, endIndexExclusive, sectionStartTime, visualBpm);
        int maxBucketInputs = buckets.Count == 0 ? 0 : buckets.Values.Max(bucket => bucket.TotalInputs);
        bool expanded = false;

        while (maxBucketInputs > totalKeyCount && visualBpm < MaxExpandedVisualBpm)
        {
            double beforeVisualBpm = visualBpm;
            visualBpm = Math.Min(MaxExpandedVisualBpm, visualBpm * 2.0);
            double beforeBeatMs = 60000.0 / Math.Max(1.0, beforeVisualBpm);
            double afterBeatMs = 60000.0 / Math.Max(1.0, visualBpm);
            debug.RecordExpansion(
                sectionIndex,
                source[startIndex].FirstSeqId,
                source[endIndexExclusive - 1].LastSeqId,
                rawBpm,
                beforeVisualBpm,
                visualBpm,
                beforeBeatMs,
                afterBeatMs,
                maxBucketInputs,
                totalKeyCount);

            buckets = BuildBuckets(source, startIndex, endIndexExclusive, sectionStartTime, visualBpm);
            maxBucketInputs = buckets.Count == 0 ? 0 : buckets.Values.Max(bucket => bucket.TotalInputs);
            expanded = true;
        }

        int rollingStartInputs = Math.Max(2, NaturalFingeringOptions.RollingOverflowStartInputs);
        bool enableRollingOverflow = NaturalFingeringOptions.EnableRollingOverflowFingering;
        int rollingOverflowBucketCount = 0;
        int maxRollingKeyCount = 0;
        int rollingCursor = 0;
        long previousRollingBucket = long.MinValue;
        string[] currentRollingKeyOrder = Array.Empty<string>();

        double beatSeconds = 60.0 / Math.Max(1.0, visualBpm);
        foreach (KeyValuePair<long, BucketState> pair in buckets)
        {
            BucketState bucket = pair.Value;
            bool useRollingForBucket = enableRollingOverflow && bucket.TotalInputs >= rollingStartInputs;
            if (useRollingForBucket)
            {
                bool startsNewRollingRun = previousRollingBucket != pair.Key - 1 || currentRollingKeyOrder.Length == 0;
                if (startsNewRollingRun)
                {
                    rollingCursor = 0;
                    currentRollingKeyOrder = BuildRollingOverflowKeyOrder(pair.Key, banks, totalKeyCount, bucket.TotalInputs);
                }
                else if (bucket.TotalInputs > currentRollingKeyOrder.Length)
                {
                    currentRollingKeyOrder = BuildRollingOverflowKeyOrder(pair.Key, banks, totalKeyCount, bucket.TotalInputs);
                }

                if (currentRollingKeyOrder.Length == 0)
                {
                    currentRollingKeyOrder = FallbackKeys;
                }

                int bucketStartCursor = rollingCursor;
                foreach (int entryIndex in bucket.EntryIndices)
                {
                    InputPlanEntry entry = source[entryIndex];
                    int count = Math.Max(1, entry.EmittedHitCount);
                    string[] entryKeys = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        entryKeys[i] = currentRollingKeyOrder[rollingCursor % currentRollingKeyOrder.Length];
                        rollingCursor++;
                    }

                    assigned[entryIndex] = CloneWithAssignedKeys(entry, entryKeys);
                }

                rollingOverflowBucketCount++;
                maxRollingKeyCount = Math.Max(maxRollingKeyCount, currentRollingKeyOrder.Length);
                previousRollingBucket = pair.Key;

                debug.RecordRollingBucket(
                    sectionIndex,
                    pair.Key,
                    sectionStartTime + pair.Key * beatSeconds,
                    rawBpm,
                    visualBpm,
                    beatSeconds * 1000.0,
                    bucket.TotalInputs,
                    currentRollingKeyOrder.Length,
                    bucketStartCursor,
                    BuildAssignedPreviewFromCursor(currentRollingKeyOrder, bucket.TotalInputs, bucketStartCursor));
                continue;
            }

            previousRollingBucket = long.MinValue;
            currentRollingKeyOrder = Array.Empty<string>();
            rollingCursor = 0;

            string[] keyOrder = BuildBucketKeyOrder(pair.Key, banks);
            RecordBucketDebugIfNeeded(
                debug,
                sectionIndex,
                pair.Key,
                sectionStartTime + pair.Key * beatSeconds,
                rawBpm,
                visualBpm,
                beatSeconds * 1000.0,
                bucket.TotalInputs,
                keyOrder,
                banks.Count,
                totalKeyCount);

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

        return new SectionStats(expanded, maxBucketInputs, visualBpm, rollingOverflowBucketCount, maxRollingKeyCount);
    }

    private static void AssignSectionRollingOverflow(
        IReadOnlyList<InputPlanEntry> source,
        InputPlanEntry[] assigned,
        int startIndex,
        int endIndexExclusive,
        IReadOnlyList<string> keyOrder)
    {
        if (keyOrder.Count == 0)
        {
            return;
        }

        int cursor = 0;
        for (int entryIndex = startIndex; entryIndex < endIndexExclusive; entryIndex++)
        {
            InputPlanEntry entry = source[entryIndex];
            int count = Math.Max(1, entry.EmittedHitCount);
            string[] entryKeys = new string[count];
            for (int i = 0; i < count; i++)
            {
                entryKeys[i] = keyOrder[cursor % keyOrder.Count];
                cursor++;
            }

            assigned[entryIndex] = CloneWithAssignedKeys(entry, entryKeys);
        }
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

    private static void RecordBucketDebugIfNeeded(
        FingeringDebugCollector debug,
        int sectionIndex,
        long bucketIndex,
        double bucketTimeSeconds,
        double rawBpm,
        double visualBpm,
        double beatMs,
        int inputCount,
        IReadOnlyList<string> keyOrder,
        int bankCount,
        int totalKeyCount)
    {
        if (!NaturalFingeringOptions.EnableFingeringLog ||
            (inputCount <= BankSize && !NaturalFingeringOptions.ShouldLog(PseudoChordUiLogMode.Verbose)))
        {
            return;
        }

        bool usesLowerBank = inputCount > BankSize;
        bool usesOppositeSide = inputCount > BankSize * 2 && bankCount >= 4;
        bool usesFoot = inputCount > BankSize * 4 && bankCount >= 6;
        bool wraps = inputCount > Math.Max(1, totalKeyCount);
        string side = PositiveModulo(bucketIndex, 2) == 1 ? "R" : "L";
        string assignedPreview = BuildAssignedPreview(keyOrder, inputCount);
        string reason = BuildDebugReason(usesLowerBank, usesOppositeSide, usesFoot, wraps);

        debug.RecordBucket(
            sectionIndex,
            bucketIndex,
            bucketTimeSeconds,
            side,
            rawBpm,
            visualBpm,
            beatMs,
            inputCount,
            assignedPreview,
            reason,
            usesLowerBank,
            usesOppositeSide,
            usesFoot,
            wraps);
    }

    private static string BuildAssignedPreview(IReadOnlyList<string> keyOrder, int inputCount)
    {
        if (keyOrder.Count == 0)
        {
            return "<none>";
        }

        int previewCount = Math.Min(Math.Max(0, inputCount), PreviewAssignedKeys);
        List<string> preview = new List<string>(previewCount);
        for (int i = 0; i < previewCount; i++)
        {
            preview.Add(keyOrder[i % keyOrder.Count]);
        }

        string suffix = inputCount > previewCount ? $",...(+{inputCount - previewCount})" : string.Empty;
        return string.Join(",", preview) + suffix;
    }

    private static string BuildAssignedPreviewFromCursor(IReadOnlyList<string> keyOrder, int inputCount, int cursor)
    {
        if (keyOrder.Count == 0)
        {
            return "<none>";
        }

        int previewCount = Math.Min(Math.Max(0, inputCount), PreviewAssignedKeys);
        List<string> preview = new List<string>(previewCount);
        for (int i = 0; i < previewCount; i++)
        {
            preview.Add(keyOrder[(cursor + i) % keyOrder.Count]);
        }

        string suffix = inputCount > previewCount ? $",...(+{inputCount - previewCount})" : string.Empty;
        return string.Join(",", preview) + suffix;
    }

    private static string BuildDebugReason(bool usesLowerBank, bool usesOppositeSide, bool usesFoot, bool wraps)
    {
        List<string> reasons = new List<string>();
        if (usesLowerBank)
        {
            reasons.Add("needs-lower-bank");
        }

        if (usesOppositeSide)
        {
            reasons.Add("needs-opposite-side");
        }

        if (usesFoot)
        {
            reasons.Add("needs-foot");
        }

        if (wraps)
        {
            reasons.Add("wraps-key-order");
        }

        return reasons.Count > 0 ? string.Join("+", reasons) : "upper-only";
    }

    private static string[] BuildRollingOverflowKeyOrder(
        long firstBucketIndex,
        IReadOnlyList<IReadOnlyList<string>> banks,
        int totalKeyCount,
        int maxBucketInputs)
    {
        bool startRight = PositiveModulo(firstBucketIndex, 2) == 1;
        List<string> order = new List<string>();

        AddInterleavedBanks(order, banks, 0, 2, startRight); // upper: L/R
        AddInterleavedBanks(order, banks, 1, 3, startRight); // lower: L/R
        if (NaturalFingeringOptions.RollingOverflowUseFeet)
        {
            AddInterleavedBanks(order, banks, 4, 5, startRight); // foot: L/R
        }

        if (order.Count == 0)
        {
            return FallbackKeys;
        }

        int targetKeyCount;
        if (maxBucketInputs <= BankSize * 2)
        {
            targetKeyCount = BankSize * 2;
        }
        else if (maxBucketInputs <= BankSize * 4 || !NaturalFingeringOptions.RollingOverflowUseFeet)
        {
            targetKeyCount = BankSize * 4;
        }
        else
        {
            targetKeyCount = BankSize * 6;
        }

        targetKeyCount = Math.Min(targetKeyCount, Math.Max(1, NaturalFingeringOptions.RollingOverflowMaxKeys));
        targetKeyCount = Math.Min(targetKeyCount, Math.Max(1, totalKeyCount));
        targetKeyCount = Math.Min(targetKeyCount, order.Count);

        return order.Take(Math.Max(1, targetKeyCount)).ToArray();
    }

    private static void AddInterleavedBanks(
        List<string> output,
        IReadOnlyList<IReadOnlyList<string>> banks,
        int leftBankIndex,
        int rightBankIndex,
        bool startRight)
    {
        IReadOnlyList<string> left = leftBankIndex >= 0 && leftBankIndex < banks.Count
            ? banks[leftBankIndex]
            : Array.Empty<string>();
        IReadOnlyList<string> right = rightBankIndex >= 0 && rightBankIndex < banks.Count
            ? banks[rightBankIndex]
            : Array.Empty<string>();

        int max = Math.Max(left.Count, right.Count);
        for (int i = 0; i < max; i++)
        {
            if (startRight)
            {
                AddIfPresent(output, right, i);
                AddIfPresent(output, left, i);
            }
            else
            {
                AddIfPresent(output, left, i);
                AddIfPresent(output, right, i);
            }
        }
    }

    private static void AddIfPresent(List<string> output, IReadOnlyList<string> keys, int index)
    {
        if (index >= 0 && index < keys.Count)
        {
            output.Add(keys[index]);
        }
    }

    private static string[] BuildBucketKeyOrder(long bucketIndex, IReadOnlyList<IReadOnlyList<string>> banks)
    {
        // v37: do not rotate through every body part on successive beats.
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
        // displayed order: P ^ \ Enter stays P ^ \ Enter. v33+ starts each beat from
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

        NaturalFingeringOptions.Load();
        double foldDownMax = Math.Max(1.0, NaturalFingeringOptions.FoldDownMaxBpm);
        double raiseUpMax = Math.Max(1.0, NaturalFingeringOptions.RaiseUpMaxBpm);

        // Downward folding and upward refinement are now runtime-editable from
        // the clean UI. Default examples: 2500 -> 625, 100 -> 400, 300 -> 300.
        while (result > foldDownMax)
        {
            result *= 0.5;
        }

        while (result * 2.0 <= raiseUpMax)
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
        public SectionStats(bool expanded, int maxBucketInputs, double finalVisualBpm, int rollingOverflowBucketCount, int rollingKeyCount)
        {
            Expanded = expanded;
            MaxBucketInputs = maxBucketInputs;
            FinalVisualBpm = finalVisualBpm;
            RollingOverflowBucketCount = rollingOverflowBucketCount;
            RollingKeyCount = rollingKeyCount;
        }

        public bool Expanded { get; }
        public int MaxBucketInputs { get; }
        public double FinalVisualBpm { get; }
        public int RollingOverflowBucketCount { get; }
        public int RollingKeyCount { get; }
    }

    private sealed class FingeringDebugCollector
    {
        private readonly int maxEvents;
        private readonly List<string> events = new List<string>();

        public FingeringDebugCollector(int maxEvents)
        {
            this.maxEvents = Math.Max(0, maxEvents);
        }

        public int LowerBankBucketCount { get; private set; }
        public int OppositeSideBucketCount { get; private set; }
        public int FootBucketCount { get; private set; }
        public int WrappedBucketCount { get; private set; }
        public int LoggedCount => events.Count;
        public int OmittedCount { get; private set; }

        public void RecordRollingBucket(
            int sectionIndex,
            long bucketIndex,
            double bucketTimeSeconds,
            double rawBpm,
            double visualBpm,
            double beatMs,
            int inputCount,
            int rollingKeyCount,
            int cursor,
            string assignedPreview)
        {
            Add(
                $"Natural fingering v66 rolling-overflow. section={sectionIndex} bucket={bucketIndex} time={bucketTimeSeconds:F6}s rawBpm={rawBpm:F3} visualBpm={visualBpm:F3} beatMs={beatMs:F3} inputs={inputCount} rollingKeys={rollingKeyCount} cursor={cursor} assigned={assignedPreview}");
        }

        public void RecordExpansion(
            int sectionIndex,
            int firstSeqId,
            int lastSeqId,
            double rawBpm,
            double beforeVisualBpm,
            double afterVisualBpm,
            double beforeBeatMs,
            double afterBeatMs,
            int maxBucketInputs,
            int totalKeyCount)
        {
            Add(
                $"Natural fingering v66 expand. section={sectionIndex} seqID={firstSeqId}-{lastSeqId} rawBpm={rawBpm:F3} visualBpm={beforeVisualBpm:F3} expandedVisualBpm={afterVisualBpm:F3} beatMs={beforeBeatMs:F3}->{afterBeatMs:F3} maxBucketInputs={maxBucketInputs} availableKeys={totalKeyCount}");
        }

        public void RecordBucket(
            int sectionIndex,
            long bucketIndex,
            double bucketTimeSeconds,
            string side,
            double rawBpm,
            double visualBpm,
            double beatMs,
            int inputCount,
            string assignedPreview,
            string reason,
            bool usesLowerBank,
            bool usesOppositeSide,
            bool usesFoot,
            bool wraps)
        {
            if (usesLowerBank)
            {
                LowerBankBucketCount++;
            }

            if (usesOppositeSide)
            {
                OppositeSideBucketCount++;
            }

            if (usesFoot)
            {
                FootBucketCount++;
            }

            if (wraps)
            {
                WrappedBucketCount++;
            }

            Add(
                $"Natural fingering v66 overflow. section={sectionIndex} bucket={bucketIndex} time={bucketTimeSeconds:F6}s side={side} rawBpm={rawBpm:F3} visualBpm={visualBpm:F3} beatMs={beatMs:F3} inputs={inputCount} assigned={assignedPreview} reason={reason}");
        }

        public void Flush(Action<string> log)
        {
            foreach (string message in events)
            {
                log(message);
            }

            if (OmittedCount > 0)
            {
                log($"Natural fingering v66 debug omitted. omittedEvents={OmittedCount} maxLoggedEvents={maxEvents}");
            }
        }

        private void Add(string message)
        {
            if (events.Count < maxEvents)
            {
                events.Add(message);
                return;
            }

            OmittedCount++;
        }
    }
}
