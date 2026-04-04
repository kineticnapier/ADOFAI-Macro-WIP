using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Parsing;

public static class AdofaiAngleConverter
{
    private const int Midspin = 999;

    public static List<double> ConvertToRelativeAngles(
        IList<double> absoluteAngles,
        IEnumerable<int> twirlIndices,
        IReadOnlyList<PauseEvent> pauseEvents)
    {
        HashSet<int> twirlSet = new(twirlIndices);

        Dictionary<int, double> pauseMap = pauseEvents
            .GroupBy(p => p.FloorIndex)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Duration));

        List <double> result = new();
        double prev = 0;
        bool twirled = false;

        for (int i = 0; i < absoluteAngles.Count; i++)
        {
            if (twirlSet.Contains(i))
            {
                twirled = !twirled;
            }

            double raw = absoluteAngles[i];

            if (raw == Midspin)
            {
                prev = NormalizeAngle(prev + 180);
                continue;
            }

            double cur = NormalizeAngle(raw);

            double rel = !twirled
                ? NormalizeRelative(prev - cur + 180)
                : NormalizeRelative(cur - prev + 180);

            if (pauseMap.TryGetValue(i, out double duration))
            {
                rel += 180.0 * duration;
            }

            result.Add(rel);
            prev = cur;
        }

        return result;
    }

    private static double NormalizeAngle(double angle)
    {
        double a = angle % 360;
        if (a < 0) a += 360;
        return a;
    }

    private static double NormalizeRelative(double angle)
    {
        double a = angle % 360;
        if (a < 0) a += 360;
        return a == 0 ? 360 : a;
    }
}