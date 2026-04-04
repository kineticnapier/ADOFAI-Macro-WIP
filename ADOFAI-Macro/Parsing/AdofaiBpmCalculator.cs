using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Parsing;

public static class AdofaiBpmCalculator
{
    private const int Midspin = 999;

    public static List<double> BuildTileBpmList(
        IList<double> angleData,
        double initialBpm,
        IEnumerable<SpeedEvent> speedEvents)
    {
        List<double> result = [];
        double currentBpm = initialBpm;

        Dictionary<int, List<SpeedEvent>> eventsByFloor = speedEvents
            .GroupBy(e => e.FloorIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (int floor = 0; floor < angleData.Count; floor++)
        {
            if (eventsByFloor.TryGetValue(floor, out List<SpeedEvent>? eventsAtFloor))
            {
                foreach (SpeedEvent ev in eventsAtFloor)
                {
                    currentBpm = ev.Type switch
                    {
                        SpeedEventType.SetBpm => ev.Value,
                        SpeedEventType.Multiply => currentBpm * ev.Value,
                        _ => throw new InvalidOperationException($"Unknown event type: {ev.Type}")
                    };
                }
            }

            if (angleData[floor] != Midspin)
            {
                result.Add(currentBpm);
            }
        }

        return result;
    }
}