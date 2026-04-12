using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public sealed class RuleBasedFingeringStrategy : IFingeringStrategy
{
    private readonly FingeringProfile _profile;
    private readonly KeyCountDecider _keyCountDecider;

    public RuleBasedFingeringStrategy(FingeringProfile profile)
    {
        _profile = profile;
        _keyCountDecider = new KeyCountDecider(profile.DensityProfile);
    }

    public IReadOnlyList<FingerKey> Generate(IReadOnlyList<ChartNote> notes)
    {
        if (notes.Count == 0)
            return [];

        IReadOnlyList<FingeringNote> fingeringNotes =
            FingeringNoteBuilder.Build(notes, _profile.PseudoChordThresholdMs);

        return GenerateCore(fingeringNotes);
    }

    private IReadOnlyList<FingerKey> GenerateCore(IReadOnlyList<FingeringNote> notes)
    {
        if (notes.Count == 0)
            return [];

        List<FingerKey> result = new(notes.Count);

        bool preferLeftForOdd = true;

        for (int i = 0; i < notes.Count; i++)
        {
            FingerKey selected;

            if (i == 0)
            {
                IReadOnlyList<FingerKey> leftPriority = GetPriorityOrderForHand(Hand.Left);
                selected = leftPriority.Count > 0 ? leftPriority[0] : _profile.UsableKeys[0];
            }
            else
            {
                FingeringNote current = notes[i];
                FingerKey previousKey = result[i - 1];

                double avgDeltaMs = GetAverageDeltaMs(notes, i, windowSize: 4);

                int requiredKeyCount =
                    _keyCountDecider.DecideRequiredKeyCount(avgDeltaMs, _profile.UsableKeys.Count);

                List<FingerKey> activeKeys =
                    GetActiveKeysForCurrentDensity(requiredKeyCount, preferLeftForOdd);

                if (requiredKeyCount % 2 == 1)
                {
                    preferLeftForOdd = !preferLeftForOdd;
                }

                int pseudoChordRunLength = current.IsPseudoChord
                    ? GetPseudoChordRunLength(notes, i)
                    : 0;

                int requiredSameHandKeys = current.IsPseudoChord
                    ? pseudoChordRunLength + 1
                    : 0;

                bool useSameHandPseudoChord =
                    ShouldUseSameHandPseudoChord(notes, i, previousKey);

                if (useSameHandPseudoChord && requiredSameHandKeys >= 2)
                {
                    activeKeys = EnsureEnoughKeysForPseudoChord(
                        activeKeys,
                        previousKey,
                        requiredSameHandKeys);
                }

                selected = ChooseKey(
                    current,
                    previousKey,
                    activeKeys,
                    useSameHandPseudoChord);

                Console.WriteLine(
                    $"i={i}, delta={current.DeltaMs:F2}, avg={avgDeltaMs:F2}, req={requiredKeyCount}, run={pseudoChordRunLength}, sameHandReq={requiredSameHandKeys}, sameHand={useSameHandPseudoChord}, active=[{string.Join(",", activeKeys)}], prev={previousKey}, selected={selected}");
            }

            result.Add(selected);
        }

        return result;
    }

    private FingerKey ChooseKey(
        FingeringNote current,
        FingerKey previousKey,
        IReadOnlyList<FingerKey> activeKeys,
        bool useSameHandPseudoChord)
    {
        List<FingerKey> candidates = [.. activeKeys];

        Hand previousHand = FingerKeyInfo.GetHand(previousKey);

        if (useSameHandPseudoChord)
        {
            List<FingerKey> sameHand =
                [.. candidates.Where(k => FingerKeyInfo.GetHand(k) == previousHand)];

            if (sameHand.Count > 0)
            {
                candidates = sameHand;
            }

            List<FingerKey> noSameKey = [.. candidates.Where(k => k != previousKey)];
            if (noSameKey.Count > 0)
            {
                candidates = noSameKey;
            }

            List<FingerKey> orderedSameHand =
                OrderCandidatesByDistanceFromKey(candidates, previousKey);

            return orderedSameHand[0];
        }
        else
        {
            List<FingerKey> differentHand =
                [.. candidates.Where(k => FingerKeyInfo.GetHand(k) != previousHand)];

            if (differentHand.Count > 0)
            {
                candidates = differentHand;
            }

            if (current.DeltaMs <= _profile.SameKeyAvoidThresholdMs)
            {
                List<FingerKey> noSameKey = [.. candidates.Where(k => k != previousKey)];
                if (noSameKey.Count > 0)
                {
                    candidates = noSameKey;
                }
            }

            List<FingerKey> ordered =
                OrderCandidatesByCenterPriority(candidates);

            return ordered[0];
        }
    }

    private double GetAverageDeltaMs(
        IReadOnlyList<FingeringNote> notes,
        int index,
        int windowSize)
    {
        int start = Math.Max(1, index - windowSize + 1);

        double sum = 0.0;
        int count = 0;

        for (int i = start; i <= index; i++)
        {
            sum += notes[i].DeltaMs;
            count++;
        }

        return count > 0 ? sum / count : double.PositiveInfinity;
    }

    private List<FingerKey> GetActiveKeysForCurrentDensity(
        int totalKeyCount,
        bool preferLeftForOdd)
    {
        IReadOnlyList<FingerKey> leftPriority = GetPriorityOrderForHand(Hand.Left);
        IReadOnlyList<FingerKey> rightPriority = GetPriorityOrderForHand(Hand.Right);

        int leftCount = totalKeyCount / 2;
        int rightCount = totalKeyCount / 2;

        if (totalKeyCount % 2 == 1)
        {
            if (preferLeftForOdd)
            {
                leftCount++;
            }
            else
            {
                rightCount++;
            }
        }

        List<FingerKey> result = [];
        result.AddRange(leftPriority.Take(leftCount));
        result.AddRange(rightPriority.Take(rightCount));
        return result;
    }

    private static int GetPseudoChordRunLength(
        IReadOnlyList<FingeringNote> notes,
        int index)
    {
        if (index < 0 || index >= notes.Count)
            return 0;

        if (!notes[index].IsPseudoChord)
            return 0;

        int start = index;
        while (start > 0 && notes[start - 1].IsPseudoChord)
        {
            start--;
        }

        int end = index;
        while (end + 1 < notes.Count && notes[end + 1].IsPseudoChord)
        {
            end++;
        }

        return end - start + 1;
    }

    private bool ShouldUseSameHandPseudoChord(
        IReadOnlyList<FingeringNote> notes,
        int index,
        FingerKey previousKey)
    {
        if (!notes[index].IsPseudoChord)
            return false;

        int runLength = GetPseudoChordRunLength(notes, index);
        int requiredSameHandKeys = runLength + 1;

        if (requiredSameHandKeys >= 4)
            return false;

        Hand hand = FingerKeyInfo.GetHand(previousKey);

        int availableKeysOfHand = _profile.UsableKeys.Count(
            k => FingerKeyInfo.GetHand(k) == hand);

        return availableKeysOfHand >= requiredSameHandKeys;
    }

    private List<FingerKey> EnsureEnoughKeysForPseudoChord(
        List<FingerKey> activeKeys,
        FingerKey previousKey,
        int requiredCount)
    {
        Hand hand = FingerKeyInfo.GetHand(previousKey);

        int count = activeKeys.Count(k => FingerKeyInfo.GetHand(k) == hand);
        if (count >= requiredCount)
        {
            return activeKeys;
        }

        IReadOnlyList<FingerKey> priority = GetPriorityOrderForHand(hand);

        foreach (FingerKey key in priority)
        {
            if (!activeKeys.Contains(key))
            {
                activeKeys.Add(key);
                count++;

                if (count >= requiredCount)
                {
                    break;
                }
            }
        }

        return activeKeys;
    }

    private IReadOnlyList<FingerKey> GetPriorityOrderForHand(Hand hand)
    {
        List<FingerKey> allKeys = _profile.UsableKeys.ToList();

        List<FingerKey> keysOfHand = allKeys
            .Where(k => FingerKeyInfo.GetHand(k) == hand)
            .ToList();

        double center = (allKeys.Count - 1) / 2.0;

        return keysOfHand
            .OrderBy(k =>
            {
                int index = allKeys.IndexOf(k);
                return Math.Abs(index - center);
            })
            .ToList();
    }

    private List<FingerKey> OrderCandidatesByCenterPriority(List<FingerKey> candidates)
    {
        List<FingerKey> allKeys = _profile.UsableKeys.ToList();
        double center = (allKeys.Count - 1) / 2.0;

        return candidates
            .OrderBy(k =>
            {
                int index = allKeys.IndexOf(k);
                return Math.Abs(index - center);
            })
            .ToList();
    }

    private List<FingerKey> OrderCandidatesByDistanceFromKey(
        List<FingerKey> candidates,
        FingerKey referenceKey)
    {
        List<FingerKey> allKeys = _profile.UsableKeys.ToList();
        int referenceIndex = allKeys.IndexOf(referenceKey);

        return candidates
            .OrderBy(k =>
            {
                int index = allKeys.IndexOf(k);
                return Math.Abs(index - referenceIndex);
            })
            .ToList();
    }
}