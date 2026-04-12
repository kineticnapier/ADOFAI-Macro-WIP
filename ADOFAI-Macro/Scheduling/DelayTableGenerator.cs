using ADOFAI_Macro.Interop;
using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Scheduling;

public sealed class DelayTableGenerator
{
    public static IReadOnlyList<double> Generate(
        IReadOnlyList<ChartNote> notes,
        double globalOffsetMs)
    {
        double[] noteTimesMs = new double[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            noteTimesMs[i] = notes[i].TimeMs;
        }

        double[] result = new double[notes.Count];

        if (NativeAcceleration.TryGenerateDelayTable(noteTimesMs, globalOffsetMs, result))
        {
            return result;
        }

        for (int i = 0; i < notes.Count; i++)
        {
            result[i] = noteTimesMs[i] + globalOffsetMs;
        }

        return result;
    }
}
