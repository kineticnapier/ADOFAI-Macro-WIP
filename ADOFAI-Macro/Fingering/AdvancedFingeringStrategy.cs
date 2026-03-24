using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public sealed class AdvancedFingeringStrategy : IFingeringStrategy
{
    private readonly KeyGroup _group;
    private readonly int _pseudoChordThreshold;
    private readonly int _streamAngle;

    public AdvancedFingeringStrategy(
        KeyGroup group,
        int pseudoChordThreshold = 30,
        int streamAngle = 45)
    {
        _group = group;
        _pseudoChordThreshold = pseudoChordThreshold;
        _streamAngle = streamAngle;
    }

    public IReadOnlyList<FingerKey> Generate(IReadOnlyList<ChartNote> notes)
    {
        if (notes.Count == 0)
            return Array.Empty<FingerKey>();

        FingerKey[] result = new FingerKey[notes.Count];

        bool lastSideRight = false; // false = 左(E/2), true = 右(P/^)
        bool useRightMain = false;

        int i = 0;
        while (i < notes.Count)
        {
            double angle = notes[i].RelativeAngle;

            if (angle == _streamAngle)
            {
                int start = i;
                while (i < notes.Count && notes[i].RelativeAngle == _streamAngle)
                {
                    i++;
                }

                int len = i - start;

                bool startRight = !lastSideRight; // 逆側開始にする

                for (int k = 0; k < len; k++)
                {
                    bool phase = ((k / 2) % 2) == 1;
                    bool odd = (k % 2) == 1;

                    if (!startRight)
                    {
                        result[start + k] = !phase
                            ? (odd ? _group.SideLeft : _group.MainLeft)
                            : (odd ? _group.SideRight : _group.MainRight);
                    }
                    else
                    {
                        result[start + k] = !phase
                            ? (odd ? _group.SideRight : _group.MainRight)
                            : (odd ? _group.SideLeft : _group.MainLeft);
                    }
                }

                FingerKey last = result[start + len - 1];
                lastSideRight = IsRight(last);

                continue;
            }

            if (angle <= _pseudoChordThreshold)
            {
                bool useRight = lastSideRight;

                FingerKey prev = i > 0 ? result[i - 1] : _group.MainLeft;
                bool prevWasStreamLike = !IsMain(prev);

                if (prevWasStreamLike)
                {
                    useRight = !lastSideRight;
                }

                result[i] = useRight ? _group.SideRight : _group.SideLeft;
                lastSideRight = useRight;
                i++;
                continue;
            }

            if (useRightMain)
            {
                result[i] = _group.MainRight;
                lastSideRight = true;
            }
            else
            {
                result[i] = _group.MainLeft;
                lastSideRight = false;
            }

            useRightMain = !useRightMain;
            i++;
        }

        return result;
    }

    private bool IsRight(FingerKey key)
        => key == _group.MainRight || key == _group.SideRight;

    private bool IsMain(FingerKey key)
        => key == _group.MainLeft || key == _group.MainRight;
}