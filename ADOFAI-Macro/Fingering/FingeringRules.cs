using ADOFAI_Macro.Fingering;
using ADOFAI_Macro.Models;

public static class FingeringRules
{
    public static IReadOnlyList<FingerKey> FilterDifferentHand(
        IReadOnlyList<FingerKey> keys,
        FingerKey previousKey)
    {
        Hand previousHand = FingerKeyInfo.GetHand(previousKey);
        return keys.Where(k => FingerKeyInfo.GetHand(k) != previousHand).ToList();
    }

    public static IReadOnlyList<FingerKey> FilterSameKeyAvoid(
        IReadOnlyList<FingerKey> keys,
        FingerKey previousKey)
    {
        return keys.Where(k => k != previousKey).ToList();
    }
}