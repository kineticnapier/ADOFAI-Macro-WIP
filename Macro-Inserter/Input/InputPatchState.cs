using UnityEngine;

namespace Macro_Inserter;

internal static class InputPatchState
{
    private static int frame = -1;
    private static int scheduledKeyCount;

    public static void BeginFrame(int keyCount)
    {
        if (keyCount <= 0)
        {
            return;
        }

        if (frame != Time.frameCount)
        {
            frame = Time.frameCount;
            scheduledKeyCount = 0;
        }

        scheduledKeyCount += keyCount;
    }

    public static bool HasScheduledInput()
    {
        return frame == Time.frameCount && scheduledKeyCount > 0;
    }

    public static bool TryGetScheduledKeyCount(out int keyCount)
    {
        if (!HasScheduledInput())
        {
            keyCount = 0;
            return false;
        }

        keyCount = scheduledKeyCount;
        return true;
    }

    public static void ClearFrame()
    {
        scheduledKeyCount = 0;
        frame = -1;
    }

    public static void Reset()
    {
        frame = -1;
        scheduledKeyCount = 0;
    }
}
