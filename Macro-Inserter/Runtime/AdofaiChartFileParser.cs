using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Macro_Inserter;

internal static class AdofaiChartFileParser
{
    private const double MidspinAngle = 999.0;

    public static IReadOnlyList<ChartFileNote> ParseNotes(string path, double macroOffsetMs)
    {
        string text = SanitizeJson(File.ReadAllText(path));
        double[] angleData = ParseAngleData(text);
        if (angleData.Length == 0)
        {
            throw new InvalidOperationException("angleData is empty.");
        }

        ChartSettings settings = ParseSettings(text);
        IReadOnlyList<ChartAction> actions = ParseActions(text);

        HashSet<int> twirlFloors = new HashSet<int>(actions
            .Where(action => string.Equals(action.EventType, "Twirl", StringComparison.Ordinal))
            .Select(action => action.Floor));

        Dictionary<int, double> pauseMap = actions
            .Where(action => string.Equals(action.EventType, "Pause", StringComparison.Ordinal))
            .GroupBy(action => action.Floor)
            .ToDictionary(group => group.Key, group => group.Sum(action => action.Duration));

        Dictionary<int, int> holdMap = actions
            .Where(action => string.Equals(action.EventType, "Hold", StringComparison.Ordinal) && action.Duration > 0.0)
            .GroupBy(action => action.Floor)
            .ToDictionary(group => group.Key, group => Math.Max(0, (int)Math.Round(group.Last().Duration)));

        Dictionary<int, int> multiPlanetMap = actions
            .Where(action => string.Equals(action.EventType, "MultiPlanet", StringComparison.Ordinal))
            .GroupBy(action => action.Floor)
            .ToDictionary(group => group.Key, group => group.Last().PlanetCount is 3 ? 3 : 2);

        Dictionary<int, bool> autoPlayMap = actions
            .Where(action => string.Equals(action.EventType, "AutoPlayTiles", StringComparison.Ordinal))
            .GroupBy(action => action.Floor - 1)
            .ToDictionary(group => group.Key, group => group.Last().Enabled);

        Dictionary<int, List<ChartAction>> speedByFloor = actions
            .Where(action => string.Equals(action.EventType, "SetSpeed", StringComparison.Ordinal))
            .GroupBy(action => action.Floor)
            .ToDictionary(group => group.Key, group => group.ToList());

        List<double> relativeAngles = new List<double>();
        List<bool> autoFlags = new List<bool>();
        List<int> sourceIndices = new List<int>();
        List<double> tileBpms = new List<double>();
        List<bool> midspinBySourceFloor = new List<bool>();

        double previousAngle = 0.0;
        bool twirled = false;
        int currentPlanetCount = 2;
        bool autoPlayEnabled = false;
        double currentBpm = settings.InitialBpm * settings.Pitch / 100.0;

        for (int floor = 0; floor < angleData.Length; floor++)
        {
            if (speedByFloor.TryGetValue(floor, out List<ChartAction>? speedEvents))
            {
                foreach (ChartAction speedEvent in speedEvents)
                {
                    currentBpm = speedEvent.SpeedType == ChartSpeedType.Multiplier
                        ? currentBpm * speedEvent.SpeedValue
                        : speedEvent.SpeedValue * settings.Pitch / 100.0;
                }
            }

            if (twirlFloors.Contains(floor))
            {
                twirled = !twirled;
            }

            if (multiPlanetMap.TryGetValue(floor, out int planetCount))
            {
                currentPlanetCount = planetCount;
            }

            if (autoPlayMap.TryGetValue(floor, out bool autoPlayState))
            {
                autoPlayEnabled = autoPlayState;
            }

            double rawAngle = angleData[floor];
            if (Math.Abs(rawAngle - MidspinAngle) < 0.001)
            {
                previousAngle = NormalizeAngle(previousAngle + 180.0);
                midspinBySourceFloor.Add(true);
                continue;
            }

            double currentAngle = NormalizeAngle(rawAngle);
            double relativeAngle = !twirled
                ? NormalizeRelative(previousAngle - currentAngle + 180.0)
                : NormalizeRelative(currentAngle - previousAngle + 180.0);

            if (pauseMap.TryGetValue(floor, out double pauseDuration))
            {
                relativeAngle += 180.0 * pauseDuration;
            }

            if (holdMap.TryGetValue(floor, out int holdDuration) && holdDuration >= 1)
            {
                relativeAngle += 360.0 * holdDuration;
            }

            if (currentPlanetCount == 3)
            {
                relativeAngle = NormalizeRelative(relativeAngle - 60.0);
            }

            relativeAngles.Add(relativeAngle);
            autoFlags.Add(autoPlayEnabled);
            sourceIndices.Add(floor);
            tileBpms.Add(currentBpm);
            midspinBySourceFloor.Add(false);
            previousAngle = currentAngle;
        }

        if (relativeAngles.Count != tileBpms.Count)
        {
            throw new InvalidOperationException("relativeAngles and tileBpms count mismatch.");
        }

        List<ChartFileNote> notes = new List<ChartFileNote>();
        double currentTimeMs = macroOffsetMs;
        for (int i = 1; i < relativeAngles.Count; i++)
        {
            double beats = relativeAngles[i] / 180.0;
            double msPerBeat = 60000.0 / tileBpms[i];
            currentTimeMs += beats * msPerBeat;

            int sourceFloor = sourceIndices[i];
            bool nearMidspin = IsSourceNearMidspin(midspinBySourceFloor, sourceFloor);
            notes.Add(new ChartFileNote(
                index: i,
                seqId: sourceFloor,
                timeSeconds: currentTimeMs / 1000.0,
                relativeAngle: relativeAngles[i],
                isAutoTile: autoFlags[i],
                isNearMidspin: nearMidspin));
        }

        return notes;
    }

    private static bool IsSourceNearMidspin(IReadOnlyList<bool> midspinBySourceFloor, int sourceFloor)
    {
        return IsMidspinSource(midspinBySourceFloor, sourceFloor - 1) ||
               IsMidspinSource(midspinBySourceFloor, sourceFloor) ||
               IsMidspinSource(midspinBySourceFloor, sourceFloor + 1);
    }

    private static bool IsMidspinSource(IReadOnlyList<bool> midspinBySourceFloor, int sourceFloor)
    {
        return sourceFloor >= 0 &&
               sourceFloor < midspinBySourceFloor.Count &&
               midspinBySourceFloor[sourceFloor];
    }

    private static ChartSettings ParseSettings(string text)
    {
        string settingsObject = ExtractNamedObject(text, "settings") ?? string.Empty;
        double bpm = TryReadDouble(settingsObject, "bpm", out double parsedBpm)
            ? parsedBpm
            : throw new InvalidOperationException("settings.bpm not found.");
        int pitch = TryReadInt(settingsObject, "pitch", out int parsedPitch) ? parsedPitch : 100;
        return new ChartSettings(bpm, pitch);
    }

    private static double[] ParseAngleData(string text)
    {
        string arrayText = ExtractNamedArray(text, "angleData")
            ?? throw new InvalidOperationException("angleData not found.");
        MatchCollection matches = Regex.Matches(arrayText, @"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?");
        double[] result = new double[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            result[i] = double.Parse(matches[i].Value, CultureInfo.InvariantCulture);
        }

        return result;
    }

    private static IReadOnlyList<ChartAction> ParseActions(string text)
    {
        string actionsArray = ExtractNamedArray(text, "actions") ?? string.Empty;
        List<ChartAction> result = new List<ChartAction>();
        foreach (string objectText in SplitTopLevelObjects(actionsArray))
        {
            if (!TryReadString(objectText, "eventType", out string eventType))
            {
                continue;
            }

            int floor = TryReadInt(objectText, "floor", out int parsedFloor) ? parsedFloor : 0;
            ChartAction action = new ChartAction(eventType, floor);

            if (string.Equals(eventType, "SetSpeed", StringComparison.Ordinal))
            {
                string speedType = TryReadString(objectText, "speedType", out string parsedSpeedType)
                    ? parsedSpeedType
                    : "Bpm";
                if (string.Equals(speedType, "Multiplier", StringComparison.OrdinalIgnoreCase))
                {
                    action.SpeedType = ChartSpeedType.Multiplier;
                    action.SpeedValue = TryReadDouble(objectText, "bpmMultiplier", out double multiplier)
                        ? multiplier
                        : 1.0;
                }
                else
                {
                    action.SpeedType = ChartSpeedType.SetBpm;
                    action.SpeedValue = TryReadDouble(objectText, "beatsPerMinute", out double bpm)
                        ? bpm
                        : 0.0;
                }
            }
            else if (string.Equals(eventType, "Pause", StringComparison.Ordinal) ||
                     string.Equals(eventType, "Hold", StringComparison.Ordinal))
            {
                action.Duration = TryReadDouble(objectText, "duration", out double duration) ? duration : 0.0;
            }
            else if (string.Equals(eventType, "MultiPlanet", StringComparison.Ordinal))
            {
                if (TryReadString(objectText, "planets", out string planets))
                {
                    action.PlanetCount = string.Equals(planets, "ThreePlanets", StringComparison.Ordinal) ? 3 : 2;
                }
            }
            else if (string.Equals(eventType, "AutoPlayTiles", StringComparison.Ordinal))
            {
                action.Enabled = TryReadBool(objectText, "enabled", out bool enabled) && enabled;
            }

            result.Add(action);
        }

        return result;
    }

    private static IEnumerable<string> SplitTopLevelObjects(string arrayContent)
    {
        int depth = 0;
        int start = -1;
        bool inString = false;
        bool escape = false;
        for (int i = 0; i < arrayContent.Length; i++)
        {
            char c = arrayContent[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                {
                    start = i;
                }

                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return arrayContent.Substring(start, i - start + 1);
                    start = -1;
                }
            }
        }
    }

    private static string? ExtractNamedArray(string text, string name)
    {
        int nameIndex = IndexOfJsonName(text, name);
        if (nameIndex < 0)
        {
            return null;
        }

        int bracket = text.IndexOf('[', nameIndex);
        if (bracket < 0)
        {
            return null;
        }

        int end = FindMatching(text, bracket, '[', ']');
        return end < 0 ? null : text.Substring(bracket + 1, end - bracket - 1);
    }

    private static string? ExtractNamedObject(string text, string name)
    {
        int nameIndex = IndexOfJsonName(text, name);
        if (nameIndex < 0)
        {
            return null;
        }

        int brace = text.IndexOf('{', nameIndex);
        if (brace < 0)
        {
            return null;
        }

        int end = FindMatching(text, brace, '{', '}');
        return end < 0 ? null : text.Substring(brace + 1, end - brace - 1);
    }

    private static int IndexOfJsonName(string text, string name)
    {
        Match match = Regex.Match(text, "\\\"" + Regex.Escape(name) + "\\\"\\s*:");
        return match.Success ? match.Index : -1;
    }

    private static int FindMatching(string text, int start, char open, char close)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static bool TryReadString(string objectText, string name, out string value)
    {
        Match match = Regex.Match(objectText, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"])*)\\\"");
        if (match.Success)
        {
            value = Regex.Unescape(match.Groups["value"].Value);
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadDouble(string objectText, string name, out double value)
    {
        Match match = Regex.Match(objectText, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*(?<value>-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)");
        if (match.Success && double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0.0;
        return false;
    }

    private static bool TryReadInt(string objectText, string name, out int value)
    {
        if (TryReadDouble(objectText, name, out double doubleValue))
        {
            value = (int)Math.Round(doubleValue);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadBool(string objectText, string name, out bool value)
    {
        Match match = Regex.Match(objectText, "\\\"" + Regex.Escape(name) + "\\\"\\s*:\\s*(?<value>true|false)", RegexOptions.IgnoreCase);
        if (match.Success && bool.TryParse(match.Groups["value"].Value, out value))
        {
            return true;
        }

        value = false;
        return false;
    }

    private static string SanitizeJson(string input)
    {
        StringBuilder sb = new StringBuilder(input.Length);
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

    private static double NormalizeAngle(double angle)
    {
        double normalized = angle % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static double NormalizeRelative(double angle)
    {
        double normalized = angle % 360.0;
        if (normalized < 0.0)
        {
            normalized += 360.0;
        }

        return Math.Abs(normalized) < 0.000001 ? 360.0 : normalized;
    }

    private sealed class ChartSettings
    {
        public ChartSettings(double initialBpm, int pitch)
        {
            InitialBpm = initialBpm;
            Pitch = pitch;
        }

        public double InitialBpm { get; }

        public int Pitch { get; }
    }

    private sealed class ChartAction
    {
        public ChartAction(string eventType, int floor)
        {
            EventType = eventType;
            Floor = floor;
            SpeedType = ChartSpeedType.SetBpm;
        }

        public string EventType { get; }

        public int Floor { get; }

        public ChartSpeedType SpeedType { get; set; }

        public double SpeedValue { get; set; }

        public double Duration { get; set; }

        public int PlanetCount { get; set; } = 2;

        public bool Enabled { get; set; }
    }

    private enum ChartSpeedType
    {
        SetBpm,
        Multiplier
    }
}

internal sealed class ChartFileNote
{
    public ChartFileNote(int index, int seqId, double timeSeconds, double relativeAngle, bool isAutoTile, bool isNearMidspin)
    {
        Index = index;
        SeqId = seqId;
        TimeSeconds = timeSeconds;
        RelativeAngle = relativeAngle;
        IsAutoTile = isAutoTile;
        IsNearMidspin = isNearMidspin;
    }

    public int Index { get; }

    public int SeqId { get; }

    public double TimeSeconds { get; }

    public double RelativeAngle { get; }

    public bool IsAutoTile { get; }

    public bool IsNearMidspin { get; }
}
