using System.Runtime.InteropServices;

namespace ADOFAI_Macro.Interop;

internal static class NativeAcceleration
{
    private const string NativeLibraryName = "adofai_native";

    [DllImport(NativeLibraryName, EntryPoint = "generate_delay_table")]
    private static extern int GenerateDelayTableNative(
        [In] double[] noteTimesMs,
        int count,
        double globalOffsetMs,
        [Out] double[] output);

    [DllImport(NativeLibraryName, EntryPoint = "resolve_key_counts")]
    private static extern int ResolveKeyCountsNative(
        [In] int[] tileIndices,
        int notesCount,
        [In] int[] rangeStartTileIndices,
        [In] int[] rangeKeyCounts,
        int rangeCount,
        int defaultKeyCount,
        [Out] int[] output);

    public static bool TryGenerateDelayTable(
        double[] noteTimesMs,
        double globalOffsetMs,
        double[] output)
    {
        try
        {
            return GenerateDelayTableNative(noteTimesMs, noteTimesMs.Length, globalOffsetMs, output) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static bool TryResolveKeyCounts(
        int[] tileIndices,
        int[] rangeStartTileIndices,
        int[] rangeKeyCounts,
        int defaultKeyCount,
        int[] output)
    {
        try
        {
            return ResolveKeyCountsNative(
                       tileIndices,
                       tileIndices.Length,
                       rangeStartTileIndices,
                       rangeKeyCounts,
                       rangeStartTileIndices.Length,
                       defaultKeyCount,
                       output) == 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
