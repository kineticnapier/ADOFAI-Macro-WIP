using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public static class FingeringNoteBuilder
{
    public static IReadOnlyList<FingeringNote> Build(
        IReadOnlyList<ChartNote> notes,
        double pseudoChordThresholdMs)
    {
        List<FingeringNote> result = new(notes.Count);

        for (int i = 0; i < notes.Count; i++)
        {
            double deltaMs;

            if (i == 0)
            {
                deltaMs = double.PositiveInfinity;
            }
            else
            {
                deltaMs = notes[i].TimeMs - notes[i - 1].TimeMs;
            }

            bool isPseudoChord = deltaMs <= pseudoChordThresholdMs;

            result.Add(new FingeringNote
            {
                Note = notes[i],
                DeltaMs = deltaMs,
                IsPseudoChord = isPseudoChord
            });
        }

        return result;
    }
}