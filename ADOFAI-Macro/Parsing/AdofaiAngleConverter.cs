using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Parsing;

public static class AdofaiAngleConverter
{
    private const int Midspin = 999;

    // 処理方法はadofaiの仕様に基づいています。
    public static List<double> ConvertToRelativeAngles(
        IList<double> absoluteAngles,
        IEnumerable<int> twirlIndices,
        IReadOnlyList<PauseEvent> pauseEvents,
        IReadOnlyList<HoldEvent> holdEvents,
        IReadOnlyList<MultiPlanetEvent> multiPlanetEvents)
    {
        HashSet<int> twirlSet = [.. twirlIndices];

        Dictionary<int, double> pauseMap = pauseEvents
            .GroupBy(p => p.FloorIndex)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Duration));

        Dictionary<int, int> holdMap = BuildHoldMap(holdEvents);

        Dictionary<int, int> multiPlanetMap = BuildMultiPlanetMap(multiPlanetEvents);

        List <double> result = [];
        double prev = 0;
        bool twirled = false;
        int currentPlanetCount = 2;

        for (int i = 0; i < absoluteAngles.Count; i++)
        {
            if (twirlSet.Contains(i))
            {
                twirled = !twirled;
            }

            if (multiPlanetMap.TryGetValue(i, out int newplanetCount))
            {
                currentPlanetCount = newplanetCount;
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

            if (holdMap.TryGetValue(i, out int holdDuration))
            {
                if (holdDuration >= 1)
                {
                    rel += 360.0 * holdDuration;
                }
            }

            if (currentPlanetCount == 3)
            {
                rel = NormalizeRelative(rel - 60.0);
            }

            result.Add(rel);
            prev = cur;
        }

        return result;
    }

    private static Dictionary<int, int> BuildHoldMap(IEnumerable<HoldEvent> holdEvents)
    {
        Dictionary<int, int> map = [];
        foreach (HoldEvent ev in holdEvents)
        {
            if (ev.Duration < 0)
                throw new InvalidOperationException(
                    $"Hold duration must be non-negative. floor={ev.FloorIndex}, duration={ev.Duration}");

            if (map.ContainsKey(ev.FloorIndex))
                throw new InvalidOperationException(
                    $"Duplicate Hold event on the same floor is not supported. floor={ev.FloorIndex}");

            map[ev.FloorIndex] = ev.Duration;
        }
        return map;
    }
    private static Dictionary<int, int> BuildMultiPlanetMap(IEnumerable<MultiPlanetEvent> multiPlanetEvents)
    {
        Dictionary<int, int> map = [];

        foreach (MultiPlanetEvent ev in multiPlanetEvents)
        {
            if (ev.PlanetCount is not (2 or 3))
                throw new InvalidOperationException(
                    $"Planet count must be 2 or 3. floor={ev.FloorIndex}, planets={ev.PlanetCount}");

            if (map.ContainsKey(ev.FloorIndex))
                throw new InvalidOperationException(
                    $"Duplicate MultiPlanet event on the same floor is not supported. floor={ev.FloorIndex}");

            map[ev.FloorIndex] = ev.PlanetCount;
        }

        return map;
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