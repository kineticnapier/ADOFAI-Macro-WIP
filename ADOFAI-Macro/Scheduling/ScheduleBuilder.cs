using System.Diagnostics;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Scheduling;

public sealed class ScheduleBuilder
{
    public static IReadOnlyList<ScheduledNote> Build(
        IReadOnlyList<ChartNote> notes,
        IReadOnlyList<double> delayedMs,
        IReadOnlyList<FingerKey> fingering)
    {
        if (notes.Count != delayedMs.Count || notes.Count != fingering.Count)
            throw new ArgumentException("Input lists must have the same length.");

        List<ScheduledNote> result = new (notes.Count);
        long frequency = Stopwatch.Frequency;

        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].IsAutoTile)
            {
                continue;
            }

            long targetTick = (long)Math.Round(delayedMs[i] * frequency / 1000.0);

            result.Add(new ScheduledNote(
                notes[i].Index,
                notes[i].TimeMs,
                fingering[i],
                targetTick,
                notes[i].RelativeAngle
            ));
        }

        return result;
    }
}