using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Scheduling;

public sealed class DelayTableGenerator
{
    public static IReadOnlyList<double> Generate(
        IReadOnlyList<ChartNote> notes,
        double globalOffsetMs)
    {
        double[] result = new double[notes.Count];

        for (int i = 0; i < notes.Count; i++)
        {
            result[i] = notes[i].TimeMs + globalOffsetMs;
        }

        return result;
    }
}