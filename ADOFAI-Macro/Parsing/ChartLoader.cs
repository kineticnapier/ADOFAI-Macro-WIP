using System.Text;
using System.Text.Json.Nodes;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Parsing;

public sealed class ChartLoader
{
    public static RawChart Load(string path)
    {
        string rawText = File.ReadAllText(path);
        string text = SanitizeJson(rawText);

        JsonNode root = JsonNode.Parse(text, 
            documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                AllowDuplicateProperties = true,
                AllowTrailingCommas = true,
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip
            })
            ?? throw new InvalidOperationException("Failed to parse JSON.");

        JsonObject obj = root.AsObject();

        JsonArray angleArray = obj["angleData"]?.AsArray()
            ?? throw new InvalidOperationException("angleData not found.");

        List<double> angleData = [.. angleArray.Select(x => x?.GetValue<double>() ?? throw new InvalidOperationException("Invalid angleData item."))];

        JsonArray actions = obj["actions"]?.AsArray()
            ?? throw new InvalidOperationException("actions not found.");

        int pitch = obj["settings"]?["pitch"]?.GetValue<int>() ?? 100;

        List<int> twirlFloors = [.. actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "Twirl")
            .Select(x => x?["floor"]?.GetValue<int>() ?? throw new InvalidOperationException("Twirl floor missing."))];

        List<SpeedEvent> speedEvents = [.. actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "SetSpeed")
            .Select(ParseSpeedEvent)];

        List<PauseEvent> pauseEvents = [.. actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "Pause")
            .Select(ParsePauseEvent)];

        List<HoldEvent> holdEvents = [.. actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "Hold")
            .Select(ParseHoldEvent)];

        List<MultiPlanetEvent> multiPlanetEvents = [.. actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "MultiPlanet")
            .Select(ParseMultiPlanetEvent)];

        List<AutoPlayTilesEvent> autoPlayTilesEvents = [.. actions
            .Where(x => x?["eventType"]?.GetValue<string>() == "AutoPlayTiles")
            .Select(ParseAutoPlayTilesEvent)];

        double initialBpm = obj["settings"]?["bpm"]?.GetValue<double>()
            ?? throw new InvalidOperationException("settings.bpm not found.");

        return new RawChart(
            initialBpm,
            pitch,
            angleData,
            twirlFloors,
            speedEvents,
            pauseEvents,
            holdEvents,
            multiPlanetEvents,
            autoPlayTilesEvents
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
    private static MultiPlanetEvent ParseMultiPlanetEvent(JsonNode? node)
    {
        if (node is null)
            throw new InvalidOperationException("MultiPlanet node is null.");
        int floorIndex = node["floor"]?.GetValue<int>()
            ?? throw new InvalidOperationException("MultiPlanet floor missing.");
        string planetStr = node["planets"]?.GetValue<string>()
            ?? throw new InvalidOperationException("MultiPlanet planetCount missing.");

        int planetCount = planetStr switch
        {
            "ThreePlanets" => 3,
            "TwoPlanets" => 2,
            _ => 2
        };
        
        return new MultiPlanetEvent(floorIndex, planetCount);
    }
    private static AutoPlayTilesEvent ParseAutoPlayTilesEvent(JsonNode? node)
    {
        if (node is null)
            throw new InvalidOperationException("AutoPlayTiles node is null.");
        int floorIndex = node["floor"]?.GetValue<int>()
            ?? throw new InvalidOperationException("AutoPlayTiles floor missing.");
        bool enabled = node["enabled"]?.GetValue<bool>()
            ?? throw new InvalidOperationException("AutoPlayTiles enabled missing.");
        return new AutoPlayTilesEvent(floorIndex, enabled);
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