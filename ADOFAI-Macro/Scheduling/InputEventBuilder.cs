using System.Diagnostics;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Scheduling;

public sealed class InputEventBuilder
{
    public static IReadOnlyList<ScheduledInputEvent> Build(
        IReadOnlyList<ScheduledNote> notes,
        double holdMs,
        double releaseLeadMs)
    {
        long releaseLeadTicks = MsToTicks(releaseLeadMs);

        List<ScheduledInputEvent> events = new(notes.Count * 2);
        Dictionary<FingerKey, ScheduledInputEvent> pendingUps = [];

        foreach (ScheduledNote note in notes.OrderBy(n => n.TargetTick))
        {
            long downTick = note.TargetTick;
            long upTick = downTick + MsToTicks(holdMs);

            if (pendingUps.TryGetValue(note.Key, out ScheduledInputEvent? previousUp))
            {
                if (previousUp.TargetTick >= downTick)
                {
                    previousUp.TargetTick = ComputeAdjustedUpTick(downTick, releaseLeadTicks);
                }
            }

            ScheduledInputEvent downEvent = new(
                downTick,
                note.Key,
                InputEventType.KeyDown
            );

            ScheduledInputEvent upEvent = new(
                upTick,
                note.Key,
                InputEventType.KeyUp
            );

            events.Add(downEvent);
            events.Add(upEvent);

            pendingUps[note.Key] = upEvent;
        }

        events.Sort(CompareEvents);
        return events;
    }

    private static int CompareEvents(ScheduledInputEvent a, ScheduledInputEvent b)
    {
        int cmp = a.TargetTick.CompareTo(b.TargetTick);
        if (cmp != 0)
        {
            return cmp;
        }

        if (a.Type != b.Type)
        {
            return a.Type == InputEventType.KeyUp ? -1 : 1;
        }

        return 0;
    }

    private static long MsToTicks(double ms)
    {
        return (long)Math.Round(ms * Stopwatch.Frequency / 1000.0);
    }

    private static long ComputeAdjustedUpTick(long downTick, long releaseLeadTicks)
    {
        long adjustedUpTick = downTick - releaseLeadTicks;

        if (adjustedUpTick >= downTick)
        {
            adjustedUpTick = downTick - 1;
        }

        return Math.Max(0, adjustedUpTick);
    }
}
