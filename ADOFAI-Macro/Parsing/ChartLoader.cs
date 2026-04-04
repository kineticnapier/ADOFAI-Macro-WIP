using System.Text;
using System.Text.Json.Nodes;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Parsing;

public sealed class ChartLoader
{
    public RawChart Load(string path)
    {
        string rawText = File.ReadAllText(path);
        string text = SanitizeJson(rawText);

        JsonNode root = JsonNode.Parse(text)
            ?? throw new InvalidOperationException("Failed to parse JSON.");

        JsonObject obj = root.AsObject();

        JsonArray angleArray = obj["angleData"]?.AsArray()
            ?? throw new InvalidOperationException("angleData not found.");

        List<double> angleData = angleArray
            .Select(x => x?.GetValue<double>() ?? throw new InvalidOperationException("Invalid angleData item."))
            .ToList();

        JsonArray actions = obj["actions"]?.AsArray()
            ?? throw new InvalidOperationException("actions not found.");

        List<int> twirlFloors = actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "Twirl")
            .Select(x => x?["floor"]?.GetValue<int>() ?? throw new InvalidOperationException("Twirl floor missing."))
            .ToList();

        List<SpeedEvent> speedEvents = actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "SetSpeed")
            .Select(ParseSpeedEvent)
            .ToList();

        List<PauseEvent> pauseEvents = actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "Pause")
            .Select(ParsePauseEvent)
            .ToList();

        List<HoldEvent> holdEvents = actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "Hold")
            .Select(ParseHoldEvent)
            .ToList();

        double initialBpm = obj["settings"]?["bpm"]?.GetValue<double>()
            ?? throw new InvalidOperationException("settings.bpm not found.");

        return new RawChart(
            initialBpm,
            angleData,
            twirlFloors,
            speedEvents,
            pauseEvents,
            holdEvents
        );
    }

    private static SpeedEvent ParseSpeedEvent(JsonNode? node)
    {
        if (node is null)
            throw new InvalidOperationException("SetSpeed node is null.");

        int floorIndex = node["floor"]?.GetValue<int>()
            ?? throw new InvalidOperationException("SetSpeed floor missing.");

        string speedType = node["speedType"]?.GetValue<string>() ?? "Bpm";

        if (speedType == "Multiplier")
        {
            double multiplier = node["bpmMultiplier"]?.GetValue<double>()
                ?? throw new InvalidOperationException("bpmMultiplier missing.");

            return new SpeedEvent(floorIndex, SpeedEventType.Multiply, multiplier);
        }
        else
        {
            double bpm = node["beatsPerMinute"]?.GetValue<double>()
                ?? throw new InvalidOperationException("beatsPerMinute missing.");

            return new SpeedEvent(floorIndex, SpeedEventType.SetBpm, bpm);
        }
    }
    
    private static PauseEvent ParsePauseEvent(JsonNode? node)
    {
        if (node is null)
            throw new InvalidOperationException("Pause node is null.");

        int floorIndex = node["floor"]?.GetValue<int>()
            ?? throw new InvalidOperationException("Pause floor missing.");
        double duration = node["duration"]?.GetValue<double>()
            ?? throw new InvalidOperationException("Pause duration missing.");
        return new PauseEvent(floorIndex, duration);
    }

    private static HoldEvent ParseHoldEvent(JsonNode? node)
    {
        if (node is null)
            throw new InvalidOperationException("Hold node is null.");
        int floorIndex = node["floor"]?.GetValue<int>()
            ?? throw new InvalidOperationException("Hold floor missing.");
        int duration = node["duration"]?.GetValue<int>()
            ?? throw new InvalidOperationException("Hold duration missing.");
        return new HoldEvent(floorIndex, duration);
    }
    private static string SanitizeJson(string input)
    {
        StringBuilder sb = new(input.Length);

        bool inString = false;
        bool escape = false;

        foreach (char c in input)
        {
            if (escape)
            {
                sb.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                sb.Append(c);
                escape = true;
                continue;
            }

            if (c == '"')
            {
                sb.Append(c);
                inString = !inString;
                continue;
            }

            if (inString && c < 0x20)
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}