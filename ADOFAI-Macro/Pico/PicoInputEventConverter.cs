using System.Diagnostics;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Pico;

public static class PicoInputEventConverter
{
    public static long TickToMicroseconds(long ticks)
    {
        return ticks * 1_000_000L / Stopwatch.Frequency;
    }

    public static IReadOnlyList<PicoInputEvent> Convert(
    IReadOnlyList<ScheduledInputEvent> inputEvents,
    long baseTick,
    long dispatchAdvanceUs)
    {
        List<ScheduledInputEvent> ordered = inputEvents
            .OrderBy(e => e.TargetTick)
            .ToList();

        if (ordered.Count == 0)
        {
            return [];
        }

        List<PicoInputEvent> result = new(ordered.Count);

        foreach (ScheduledInputEvent e in ordered)
        {
            long deltaTick = e.TargetTick - baseTick;
            long offsetUsLong = TickToMicroseconds(deltaTick) - dispatchAdvanceUs;

            if (offsetUsLong < 0)
            {
                offsetUsLong = 0;
            }

            if (offsetUsLong > uint.MaxValue)
            {
                throw new InvalidOperationException("offset_us が uint の範囲を超えました。");
            }

            string keyName = ConvertFingerKey(e.Key);
            string eventType = ConvertInputEventType(e.Type);

            result.Add(new PicoInputEvent((uint)offsetUsLong, keyName, eventType));
        }

        return result;
    }

    private static string ConvertInputEventType(InputEventType type)
    {
        return type switch
        {
            InputEventType.KeyDown => "DOWN",
            InputEventType.KeyUp => "UP",
            _ => throw new NotSupportedException($"未対応の入力イベント種別です: {type}")
        };
    }

    private static string ConvertFingerKey(FingerKey key)
    {
        return key switch
        {
            FingerKey.A => "A",
            FingerKey.B => "B",
            FingerKey.C => "C",
            FingerKey.D => "D",
            FingerKey.E => "E",
            FingerKey.F => "F",
            FingerKey.G => "G",
            FingerKey.H => "H",
            FingerKey.I => "I",
            FingerKey.J => "J",
            FingerKey.K => "K",
            FingerKey.L => "L",
            FingerKey.M => "M",
            FingerKey.N => "N",
            FingerKey.O => "O",
            FingerKey.P => "P",
            FingerKey.Q => "Q",
            FingerKey.R => "R",
            FingerKey.S => "S",
            FingerKey.T => "T",
            FingerKey.U => "U",
            FingerKey.V => "V",
            FingerKey.W => "W",
            FingerKey.X => "X",
            FingerKey.Y => "Y",
            FingerKey.Z => "Z",

            FingerKey.D0 => "0",
            FingerKey.D1 => "1",
            FingerKey.D2 => "2",
            FingerKey.D3 => "3",
            FingerKey.D4 => "4",
            FingerKey.D5 => "5",
            FingerKey.D6 => "6",
            FingerKey.D7 => "7",
            FingerKey.D8 => "8",
            FingerKey.D9 => "9",

            _ => throw new NotSupportedException($"未対応のキー: {key}")
        };
    }
}