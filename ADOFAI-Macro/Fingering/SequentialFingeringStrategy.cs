using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public sealed class SequentialFingeringStrategy : IFingeringStrategy
{
    //<summary>
    // 順繰りにキーを押していく戦略です
    //</summary>
    private readonly IReadOnlyList<FingerKey> _keyOrder;

    public SequentialFingeringStrategy(IReadOnlyList<FingerKey> keyOrder)
    {
        if (keyOrder.Count == 0)
            throw new ArgumentException("keyOrder must not be empty.");

        _keyOrder = keyOrder;
    }

    public IReadOnlyList<FingerKey> Generate(IReadOnlyList<ChartNote> notes)
    {
        FingerKey[] result = new FingerKey[notes.Count];

        for (int i = 0; i < notes.Count; i++)
        {
            result[i] = _keyOrder[i % _keyOrder.Count];
        }

        return result;
    }
}