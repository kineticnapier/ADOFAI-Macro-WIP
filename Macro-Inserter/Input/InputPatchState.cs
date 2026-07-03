using UnityEngine;

namespace Macro_Inserter;

internal static class InputPatchState
{
    private const int MaxPendingFrames = 8;
    private const int MaxSyntheticHitFrames = 2;

    private static int scheduledFrame = -1;
    private static int scheduledKeyCount;

    private static int syntheticHitFrame = -1;
    private static int syntheticHitBudget;

    public static void BeginFrame(int keyCount)
    {
        if (keyCount <= 0)
        {
            return;
        }

        int currentFrame = Time.frameCount;
        if (scheduledKeyCount <= 0 || IsPendingExpired(currentFrame))
        {
            scheduledFrame = currentFrame;
            scheduledKeyCount = keyCount;
            return;
        }

        if (scheduledFrame == currentFrame)
        {
            scheduledKeyCount += keyCount;
            return;
        }

        // The scheduler can retry the same entry on later PlayerControl_Update prefixes
        // until the game consumes the virtual input. Keep the pending input alive, but do
        // not accumulate one missed attempt per frame into a huge fake chord.
        scheduledFrame = currentFrame;
        scheduledKeyCount = Mathf.Max(scheduledKeyCount, keyCount);
    }

    public static bool HasScheduledInput()
    {
        int currentFrame = Time.frameCount;
        if (scheduledKeyCount <= 0)
        {
            return false;
        }

        if (IsPendingExpired(currentFrame))
        {
            ClearScheduledInput();
            return false;
        }

        return true;
    }

    public static bool TryGetScheduledKeyCount(out int keyCount)
    {
        if (!HasScheduledInput())
        {
            keyCount = 0;
            return false;
        }

        keyCount = scheduledKeyCount;
        ClearScheduledInput();
        BeginSyntheticHitInputEventWindow(keyCount);
        return true;
    }

    public static void AllowSyntheticHitInputEvents(int keyCount)
    {
        BeginSyntheticHitInputEventWindow(keyCount);
    }

    public static bool TryConsumeSyntheticHitInputEvent(object? state, out int remainingBudget)
    {
        remainingBudget = 0;

        // Hit(false) gates on HitInputEvent(false, InputEventState.Down). Other
        // calls such as Up can occur around hold/update logic. Consuming the
        // synthetic budget on Up made v17 random: sometimes a Down happened in the
        // window, sometimes the budget was burned first. Only Down belongs to the
        // virtual key press.
        if (!IsDownState(state))
        {
            return false;
        }

        int currentFrame = Time.frameCount;
        if (syntheticHitBudget <= 0)
        {
            return false;
        }

        if (IsSyntheticHitExpired(currentFrame))
        {
            ClearSyntheticHitInputEvents();
            return false;
        }

        syntheticHitBudget--;
        remainingBudget = syntheticHitBudget;
        return true;
    }

    public static void ClearFrame()
    {
        // Older versions cleared unconditionally in PlayerControl_Update postfix.
        // That loses inputs that were queued in the macro prefix but not consumed by
        // the game in the same PlayerControl_Update body. Keep pending input alive
        // until CountValidKeysPressed consumes it, with a small expiry guard.
        int currentFrame = Time.frameCount;
        if (IsPendingExpired(currentFrame))
        {
            ClearScheduledInput();
        }

        if (IsSyntheticHitExpired(currentFrame))
        {
            ClearSyntheticHitInputEvents();
        }
    }

    public static void Reset()
    {
        ClearScheduledInput();
        ClearSyntheticHitInputEvents();
    }

    public static string DebugSnapshot()
    {
        return $"scheduledFrame={scheduledFrame} syntheticHitFrame={syntheticHitFrame} currentFrame={Time.frameCount} scheduledKeyCount={scheduledKeyCount} syntheticHitBudget={syntheticHitBudget}";
    }

    private static void BeginSyntheticHitInputEventWindow(int keyCount)
    {
        int currentFrame = Time.frameCount;
        syntheticHitFrame = currentFrame;

        // HitAutoFloors() adds one keyTime per valid key, then UpdateHoldKeys() calls
        // Hit(false), whose first gate is HitInputEvent(false, Down). Because virtual
        // input has no real RDInput event, those HitInputEvent calls must be accepted
        // too. Add slack for midspin-generated keyTimes inside Hit(false).
        int requestedBudget = Mathf.Max(1, keyCount * 4 + 8);
        syntheticHitBudget = Mathf.Max(syntheticHitBudget, requestedBudget);
    }

    private static bool IsDownState(object? state)
    {
        return state != null && string.Equals(state.ToString(), "Down", System.StringComparison.OrdinalIgnoreCase);
    }

    private static void ClearScheduledInput()
    {
        scheduledFrame = -1;
        scheduledKeyCount = 0;
    }

    private static void ClearSyntheticHitInputEvents()
    {
        syntheticHitFrame = -1;
        syntheticHitBudget = 0;
    }

    private static bool IsPendingExpired(int currentFrame)
    {
        return scheduledFrame >= 0 && currentFrame - scheduledFrame > MaxPendingFrames;
    }

    private static bool IsSyntheticHitExpired(int currentFrame)
    {
        return syntheticHitFrame >= 0 && currentFrame - syntheticHitFrame > MaxSyntheticHitFrames;
    }
}
