using System.Text.Json;
using System.Text.Json.Nodes;
namespace AdofaiPauseConverter;

public static class AdofaiPauseConverter
{
    static void Main(string[] args)
    {
        try
        {
            if (args.Length != 2)
            {
                string? input = Console.ReadLine();
                string? output = Console.ReadLine();
                if (input != null && output != null)
                {
                    ConvertPauseToSpeedChanges(input, output);
                }
            }
            else
            {
                ConvertPauseToSpeedChanges(args[0], args[1]);
                Console.WriteLine("変換が完了しました。");
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"エラーが発生しました: {ex.Message}");
            Console.WriteLine($"スタックトレース: {ex.StackTrace}");
        }
    }

    private const int Midspin = 999;

    public static void ConvertPauseToSpeedChanges(string inputPath, string outputPath)
    {
        string json = File.ReadAllText(inputPath);

        JsonNode? root = JsonNode.Parse(json, new JsonNodeOptions
        {
            PropertyNameCaseInsensitive = false
        });

        if (root is not JsonObject rootObj)
            throw new InvalidOperationException("ルートが JsonObject ではありません。");

        if (rootObj["actions"] is not JsonArray actions)
            throw new InvalidOperationException("actions が見つかりません。");

        if (rootObj["angleData"] is not JsonArray angleDataNode)
            throw new InvalidOperationException("angleData が見つかりません。");

        List<double> angleData = angleDataNode
            .Select(ParseAngleDataItem)
            .ToList();

        int maxFloor = angleData.Count - 1;

        // Pause は通常同じ floor に複数置けない前提なので、そのまま 1 個ずつ処理
        List<JsonObject> pauseEvents = actions
            .OfType<JsonObject>()
            .Where(IsPauseEvent)
            .OrderBy(GetFloor)
            .ToList();

        foreach (JsonObject pause in pauseEvents)
        {
            int floor = GetFloor(pause);
            double duration = GetPauseDuration(pause);

            if (floor < 0 || floor > maxFloor)
                continue;

            double tileAngle = GetTileTravelAngle(actions, angleData, floor);
            double factor = tileAngle / (tileAngle + 180.0 * duration);
            double inverseFactor = 1.0 / factor;

            ApplyFactorToCurrentTile(actions, floor, factor);

            if (floor + 1 <= maxFloor)
            {
                ApplyInverseFactorToNextTile(actions, floor + 1, inverseFactor);
            }
        }

        RemoveAllPauseEvents(actions);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(outputPath, rootObj.ToJsonString(options));
    }

    private static void ApplyFactorToCurrentTile(JsonArray actions, int floor, double factor)
    {
        JsonObject? speed = FindLastSetSpeedAtFloor(actions, floor);

        if (speed is null)
        {
            // なし-*
            actions.Add(CreateMultiplierSetSpeed(floor, factor));
            return;
        }

        switch (GetSetSpeedKind(speed))
        {
            case SetSpeedKind.Bpm:
                {
                    // 数値-*
                    double bpm = GetDouble(speed, "beatsPerMinute");
                    speed["beatsPerMinute"] = bpm * factor;
                    break;
                }

            case SetSpeedKind.Multiplier:
                {
                    // 倍率-*
                    double mul = GetDouble(speed, "bpmMultiplier");
                    speed["bpmMultiplier"] = mul * factor;
                    break;
                }

            default:
                throw new InvalidOperationException($"floor={floor} の SetSpeed の種類を判定できません。");
        }
    }

    private static void ApplyInverseFactorToNextTile(JsonArray actions, int nextFloor, double inverseFactor)
    {
        JsonObject? speed = FindLastSetSpeedAtFloor(actions, nextFloor);

        if (speed is null)
        {
            // *-なし
            actions.Add(CreateMultiplierSetSpeed(nextFloor, inverseFactor));
            return;
        }

        switch (GetSetSpeedKind(speed))
        {
            case SetSpeedKind.Bpm:
                // *-数値
                // 次タイルが数値指定なら何もしない
                break;

            case SetSpeedKind.Multiplier:
                {
                    // *-倍率
                    double mul = GetDouble(speed, "bpmMultiplier");
                    speed["bpmMultiplier"] = mul * inverseFactor;
                    break;
                }

            default:
                throw new InvalidOperationException($"floor={nextFloor} の SetSpeed の種類を判定できません。");
        }
    }

    private static JsonObject? FindLastSetSpeedAtFloor(JsonArray actions, int floor)
    {
        // 同じ floor に複数 SetSpeed があるケースに備えて最後のものを採用
        return actions
            .OfType<JsonObject>()
            .Where(x => IsSetSpeedEvent(x) && GetFloor(x) == floor)
            .LastOrDefault();
    }

    private static void RemoveAllPauseEvents(JsonArray actions)
    {
        for (int i = actions.Count - 1; i >= 0; i--)
        {
            if (actions[i] is JsonObject obj && IsPauseEvent(obj))
            {
                actions.RemoveAt(i);
            }
        }
    }

    private static JsonObject CreateMultiplierSetSpeed(int floor, double multiplier)
    {
        return new JsonObject
        {
            ["floor"] = floor,
            ["eventType"] = "SetSpeed",
            ["speedType"] = "Multiplier",
            ["bpmMultiplier"] = multiplier,
            ["angleOffset"] = 0,
            ["eventTag"] = ""
        };
    }

    private static bool IsPauseEvent(JsonObject obj)
    {
        return string.Equals(
            obj["eventType"]?.GetValue<string>(),
            "Pause",
            StringComparison.Ordinal);
    }

    private static bool IsSetSpeedEvent(JsonObject obj)
    {
        return string.Equals(
            obj["eventType"]?.GetValue<string>(),
            "SetSpeed",
            StringComparison.Ordinal);
    }

    private static bool IsTwirlEvent(JsonObject obj)
    {
        return string.Equals(
            obj["eventType"]?.GetValue<string>(),
            "Twirl",
            StringComparison.Ordinal);
    }

    private static int GetFloor(JsonObject obj)
    {
        JsonNode? node = obj["floor"];
        if (node is null)
            throw new InvalidOperationException("floor がありません。");

        return node.GetValue<int>();
    }

    private static double GetPauseDuration(JsonObject obj)
    {
        JsonNode? node = obj["duration"];
        if (node is null)
            throw new InvalidOperationException("Pause の duration がありません。");

        return node.GetValue<double>();
    }

    private static double GetDouble(JsonObject obj, string propertyName)
    {
        JsonNode? node = obj[propertyName];
        if (node is null)
            throw new InvalidOperationException($"{propertyName} がありません。");

        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out double d))
                return d;

            if (value.TryGetValue<int>(out int i))
                return i;
        }

        throw new InvalidOperationException($"{propertyName} を数値として読めません。");
    }

    private static SetSpeedKind GetSetSpeedKind(JsonObject obj)
    {
        string? speedType = obj["speedType"]?.GetValue<string>();

        if (string.Equals(speedType, "Bpm", StringComparison.OrdinalIgnoreCase))
            return SetSpeedKind.Bpm;

        if (string.Equals(speedType, "Multiplier", StringComparison.OrdinalIgnoreCase))
            return SetSpeedKind.Multiplier;

        // speedType が無い譜面への保険
        if (obj["beatsPerMinute"] is not null)
            return SetSpeedKind.Bpm;

        if (obj["bpmMultiplier"] is not null)
            return SetSpeedKind.Multiplier;

        return SetSpeedKind.Unknown;
    }

    private static double ParseAngleDataItem(JsonNode? node)
    {
        if (node is null)
            throw new InvalidOperationException("angleData に null があります。");

        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out double d))
                return d;

            if (value.TryGetValue<int>(out int i))
                return i;
        }

        throw new InvalidOperationException("angleData の要素を数値として読めません。");
    }

    private static double GetTileTravelAngle(JsonArray actions, IReadOnlyList<double> angleData, int floor)
    {
        if (floor < 0 || floor >= angleData.Count)
            throw new ArgumentOutOfRangeException(nameof(floor));

        if (angleData[floor] == Midspin)
        {
            throw new InvalidOperationException(
                $"floor={floor} は 999(midspin) です。この floor の Pause には未対応です。");
        }

        double relativeAngle = GetRelativeAngleConsideringTwirl(angleData, GetTwirlSet(actions), floor);

        if (IsThreePlanetsAtFloor(actions, floor))
        {
            relativeAngle = NormalizeAngle(relativeAngle - 60.0);
        }

        return relativeAngle;
    }

    private static HashSet<int> GetTwirlSet(JsonArray actions)
    {
        return actions
            .OfType<JsonObject>()
            .Where(IsTwirlEvent)
            .Select(GetFloor)
            .ToHashSet();
    }

    private static double GetRelativeAngleConsideringTwirl(
        IReadOnlyList<double> angleData,
        HashSet<int> twirlSet,
        int floor)
    {
        int prevFloor = GetPreviousPlayableFloor(angleData, floor);
        double current = angleData[floor];
        double prev = prevFloor >= 0 ? angleData[prevFloor] : 0.0;

        double angle = NormalizeAngle(prev - current + 180.0);

        bool twirled = false;
        for (int i = 0; i <= floor; i++)
        {
            if (twirlSet.Contains(i))
            {
                twirled = !twirled;
            }
        }

        if (twirled)
        {
            angle = NormalizeAngle(360.0 - angle);
        }

        return angle;
    }

    private static int GetPreviousPlayableFloor(IReadOnlyList<double> angleData, int floor)
    {
        for (int i = floor - 1; i >= 0; i--)
        {
            if (angleData[i] != Midspin)
                return i;
        }

        return -1;
    }

    private static bool IsThreePlanetsAtFloor(JsonArray actions, int floor)
    {
        // デフォルトは 2 惑星
        bool isThreePlanets = false;

        foreach (JsonObject action in actions.OfType<JsonObject>().OrderBy(GetFloor))
        {
            int actionFloor = GetFloor(action);
            if (actionFloor > floor)
                break;

            if (!IsPlanetCountEvent(action))
                continue;

            if (TryReadPlanetCount(action, out int count))
            {
                isThreePlanets = count == 3;
            }
        }

        return isThreePlanets;
    }

    private static bool IsPlanetCountEvent(JsonObject obj)
    {
        string? eventType = obj["eventType"]?.GetValue<string>();

        return string.Equals(eventType, "MultiPlanet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "SetPlanet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "Planet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "SetPlanets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadPlanetCount(JsonObject obj, out int count)
    {
        count = 2;

        // よくある文字列系
        string? planets = obj["planets"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(planets))
        {
            string p = planets.Trim();

            if (string.Equals(p, "ThreePlanets", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "Three", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "3", StringComparison.OrdinalIgnoreCase))
            {
                count = 3;
                return true;
            }

            if (string.Equals(p, "TwoPlanets", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "Two", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "2", StringComparison.OrdinalIgnoreCase))
            {
                count = 2;
                return true;
            }
        }

        // 数値系の保険
        if (obj["planetCount"] is JsonValue planetCountValue)
        {
            if (planetCountValue.TryGetValue<int>(out int c))
            {
                count = c;
                return true;
            }
        }

        if (obj["count"] is JsonValue countValue)
        {
            if (countValue.TryGetValue<int>(out int c))
            {
                count = c;
                return true;
            }
        }

        return false;
    }

    private static double NormalizeAngle(double angle)
    {
        double result = angle % 360.0;

        if (result < 0)
            result += 360.0;

        if (result == 0.0)
            result = 360.0;

        return result;
    }

    private enum SetSpeedKind
    {
        Unknown,
        Bpm,
        Multiplier
    }
}