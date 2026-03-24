using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public sealed class PseudoChordFingeringStrategy : IFingeringStrategy
{
    private readonly KeyGroup _group;
    private readonly int _pseudoChordThreshold;

    public PseudoChordFingeringStrategy(KeyGroup group, int pseudoChordThreshold = 30)
    {
        _group = group;
        _pseudoChordThreshold = pseudoChordThreshold;
    }

    public IReadOnlyList<FingerKey> Generate(IReadOnlyList<ChartNote> notes)
    {
        if (notes.Count == 0)
            return Array.Empty<FingerKey>();

        FingerKey[] result = new FingerKey[notes.Count];

        bool useRightMain = false;
        bool lastMainWasRight = false;

        for (int i = 0; i < notes.Count; i++)
        {
            bool isPseudoChord = notes[i].RelativeAngle <= _pseudoChordThreshold;

            if (!isPseudoChord)
            {
                if (useRightMain)
                {
                    result[i] = _group.MainRight;
                    lastMainWasRight = true;
                }
                else
                {
                    result[i] = _group.MainLeft;
                    lastMainWasRight = false;
                }

                useRightMain = !useRightMain;
            }
            else
            {
                result[i] = lastMainWasRight ? _group.SideRight : _group.SideLeft;
            }
        }

        return result;
    }
}