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