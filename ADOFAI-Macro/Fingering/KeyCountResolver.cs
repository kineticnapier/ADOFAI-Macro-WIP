using ADOFAI_Macro.Interop;
using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Fingering;

public static class KeyCountResolver
{
    public static IReadOnlyList<int> ResolvePerNoteKeyCounts(
        IReadOnlyList<ChartNote> notes,
        IReadOnlyList<KeyCountRange> ranges,
        int defaultKeyCount)
    {
        int[] result = new int[notes.Count];

        List<KeyCountRange> ordered = ranges
            .OrderBy(x => x.StartTileIndex)
            .ToList();

        int[] tileIndices = new int[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            tileIndices[i] = notes[i].TileIndex;
        }

        int[] rangeStarts = new int[ordered.Count];
        int[] rangeKeyCounts = new int[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            rangeStarts[i] = ordered[i].StartTileIndex;
            rangeKeyCounts[i] = ordered[i].KeyCount;
        }

        if (NativeAcceleration.TryResolveKeyCounts(
                tileIndices,
                rangeStarts,
                rangeKeyCounts,
                defaultKeyCount,
                result))
        {
            return result;
        }

        int rangeIndex = 0;
        int currentKeyCount = defaultKeyCount;

        for (int i = 0; i < notes.Count; i++)
        {
            while (rangeIndex < ordered.Count &&
                   ordered[rangeIndex].StartTileIndex <= notes[i].TileIndex)
            {
                currentKeyCount = ordered[rangeIndex].KeyCount;
                rangeIndex++;
            }

            result[i] = currentKeyCount;
        }

        return result;
    }
}
