using UnityEngine;

namespace Macro_Inserter;

internal static class InputPatchState
{
    private static int validUntilFrame = -1;
    private static int scheduledKeyCount;

    public static void BeginFrame(int keyCount)
    {
        if (keyCount <= 0)
        {
            return;
        }

        int currentFrame = Time.frameCount;
        if (currentFrame > validUntilFrame)
        {
            scheduledKeyCount = 0;
        }

        validUntilFrame = currentFrame + 1;
        scheduledKeyCount += keyCount;
    }

    public static bool HasScheduledInput()
    {
        return Time.frameCount <= validUntilFrame && scheduledKeyCount > 0;
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
        validUntilFrame = -1;
    }

    public static void Reset()
    {
        validUntilFrame = -1;
        scheduledKeyCount = 0;
    }
}
