using ADOFAI_Macro.Fingering;
using ADOFAI_Macro.Models;

public sealed class KeyLimitedSequentialFingeringStrategy : IFingeringStrategy
{
    private readonly IReadOnlyList<FingerKey> _baseKeys;
    private readonly IReadOnlyList<int> _keyCountsPerTile;

    public KeyLimitedSequentialFingeringStrategy(
        IReadOnlyList<FingerKey> baseKeys,
        IReadOnlyList<int> keyCountsPerTile
    )
    {
        if (baseKeys.Count == 0)
        {
            throw new ArgumentException("Base key list must not be empty.", nameof(baseKeys));
        }

        _baseKeys = baseKeys;
        _keyCountsPerTile = keyCountsPerTile;
    }

    public IReadOnlyList<FingerKey> Generate(IReadOnlyList<ChartNote> notes)
    {
        if (notes.Count != _keyCountsPerTile.Count)
        {
            throw new ArgumentException("notes.Count and keyCountsPerTile.Count must match.");
        }

        FingerKey[] result = new FingerKey[notes.Count];

        Dictionary<int, int> cursorByKeyCount = new();

        for (int i = 0; i < notes.Count; i++)
        {
            int keyCount = _keyCountsPerTile[i];

            if (keyCount <= 0 || keyCount > _baseKeys.Count)
            {
                throw new InvalidOperationException(
                    $"Invalid key count {keyCount} at tile {i}. Base key count is {_baseKeys.Count}.");
            }

            if (!cursorByKeyCount.TryGetValue(keyCount, out int cursor))
            {
                cursor = 0;
            }

            result[i] = _baseKeys[cursor % keyCount];
            cursorByKeyCount[keyCount] = cursor + 1;
        }

        return result;
    }
}