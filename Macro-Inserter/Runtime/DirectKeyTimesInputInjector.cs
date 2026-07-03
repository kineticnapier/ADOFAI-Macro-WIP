using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Macro_Inserter;

internal static class DirectKeyTimesInputInjector
{
    private static MethodInfo? simulatedPlayerControlUpdate;
    private static bool forcingSimulation;
    private static bool warnedMissingSimulationMethod;
    private static MethodInfo? directHitMethod;
    private static bool warnedMissingDirectHitMethod;

    public static int InvokeDirectHitOnly(object? controller, int syntheticHitBudget, Action<string>? log)
    {
        if (controller == null)
        {
            log?.Invoke("plainSingleDirectHit failed: scrController instance was not found.");
            return -1;
        }

        int beforeFloor = ReadCurrentFloorSeqId(controller);
        ClearQueuedKeyTimes(controller, log);
        InputPatchState.AllowSyntheticHitInputEvents(Math.Max(1, syntheticHitBudget));

        if (!TryInvokeDirectHit(controller, log))
        {
            return ReadCurrentFloorSeqId(controller);
        }

        int afterFloor = ReadCurrentFloorSeqId(controller);
        if (afterFloor > beforeFloor)
        {
            log?.Invoke($"plainSingleDirectHit advanced. beforeFloor={beforeFloor} afterFloor={afterFloor} syntheticHitBudget={Math.Max(1, syntheticHitBudget)}");
        }
        else
        {
            log?.Invoke($"plainSingleDirectHit did not advance. beforeFloor={beforeFloor} afterFloor={afterFloor} syntheticHitBudget={Math.Max(1, syntheticHitBudget)}");
        }

        return afterFloor;
    }

    public static int Inject(object? controller, int keyCount, bool forceSimulation, bool allowDirectHitFallback, Action<string>? log)
    {
        if (controller == null)
        {
            log?.Invoke("directKeyTimes failed: scrController instance was not found.");
            return -1;
        }

        int beforeFloor = ReadCurrentFloorSeqId(controller);
        if (!TryQueueKeyTimes(controller, keyCount, log, out int beforeQueueCount, out int addedCount, out int afterQueueCount))
        {
            return beforeFloor;
        }

        InputPatchState.AllowSyntheticHitInputEvents(Math.Max(1, keyCount));

        if (forceSimulation)
        {
            ForceSimulatedPlayerControlUpdate(controller, log);
        }

        int afterFloor = ReadCurrentFloorSeqId(controller);
        if (afterFloor > beforeFloor || !allowDirectHitFallback)
        {
            return afterFloor;
        }

        // Some normal one-hit tiles do not consume keyTimes immediately because the
        // game's hold/input gate rejects UpdateHoldKeys for that frame. If we leave
        // the stale queued key in keyTimes, later retries only top up to the same
        // count and the scheduler can get stuck on a single floor. For a plain
        // one-hit, non-compressed, non-midspin entry, fall back to the game's
        // Hit(false) after clearing our stale keyTimes queue.
        ClearQueuedKeyTimes(controller, log);
        InputPatchState.AllowSyntheticHitInputEvents(Math.Max(1, keyCount) * 2);

        int fallbackBeforeFloor = ReadCurrentFloorSeqId(controller);
        if (TryInvokeDirectHit(controller, log))
        {
            afterFloor = ReadCurrentFloorSeqId(controller);
            if (afterFloor > fallbackBeforeFloor)
            {
                log?.Invoke($"directKeyTimes fallback DirectHit advanced. beforeFloor={fallbackBeforeFloor} afterFloor={afterFloor} keyCount={Math.Max(1, keyCount)}");
            }
        }
        else
        {
            afterFloor = ReadCurrentFloorSeqId(controller);
        }

        return afterFloor;
    }

    public static int ReadCurrentFloorSeqId(object? controller)
    {
        if (controller == null)
        {
            return -1;
        }

        object? floor = ReflectionCache.ReadMember(controller, "currFloor", "currentFloor");
        if (floor == null)
        {
            return -1;
        }

        return ReflectionCache.TryReadInt(floor, out int seqId, "seqID", "seqId", "SeqId") ? seqId : -1;
    }

    private static bool TryQueueKeyTimes(
        object controller,
        int keyCount,
        Action<string>? log,
        out int beforeQueueCount,
        out int addedCount,
        out int afterQueueCount)
    {
        beforeQueueCount = 0;
        addedCount = 0;
        afterQueueCount = 0;

        object? rawKeyTimes = ReflectionCache.ReadMember(controller, "keyTimes");
        IList? keyTimes = rawKeyTimes as IList;
        if (keyTimes == null)
        {
            log?.Invoke($"directKeyTimes failed: keyTimes list was not found. rawType={rawKeyTimes?.GetType().FullName ?? "<null>"}");
            return false;
        }

        beforeQueueCount = keyTimes.Count;

        // If a previous retry already queued keys and the game has not consumed them,
        // do not add a new fake key every frame. Top up to the requested keyCount only.
        int targetCount = Math.Max(1, keyCount);
        int toAdd = Math.Max(0, targetCount - beforeQueueCount);
        double now = Time.timeAsDouble;
        for (int i = 0; i < toAdd; i++)
        {
            keyTimes.Add(now);
            addedCount++;
        }

        afterQueueCount = keyTimes.Count;
        return true;
    }

    private static void ClearQueuedKeyTimes(object controller, Action<string>? log)
    {
        object? rawKeyTimes = ReflectionCache.ReadMember(controller, "keyTimes");
        if (rawKeyTimes is not IList keyTimes)
        {
            return;
        }

        int staleCount = keyTimes.Count;
        if (staleCount <= 0)
        {
            return;
        }

        keyTimes.Clear();
        log?.Invoke($"directKeyTimes cleared stale keyTimes before DirectHit fallback. staleCount={staleCount}");
    }

    private static bool TryInvokeDirectHit(object controller, Action<string>? log)
    {
        if (directHitMethod == null)
        {
            directHitMethod = ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        }

        if (directHitMethod == null)
        {
            if (!warnedMissingDirectHitMethod)
            {
                warnedMissingDirectHitMethod = true;
                log?.Invoke("directKeyTimes fallback failed: scrController.Hit(bool) was not found.");
            }
            return false;
        }

        try
        {
            object? result = directHitMethod.Invoke(controller, new object?[] { false });
            return result is bool accepted && accepted;
        }
        catch (Exception ex)
        {
            log?.Invoke($"directKeyTimes fallback DirectHit failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void ForceSimulatedPlayerControlUpdate(object controller, Action<string>? log)
    {
        if (forcingSimulation)
        {
            return;
        }

        if (simulatedPlayerControlUpdate == null)
        {
            simulatedPlayerControlUpdate = ReflectionCache.FindMethod("scrController", "Simulated_PlayerControl_Update", typeof(ulong?));
        }

        if (simulatedPlayerControlUpdate == null)
        {
            if (!warnedMissingSimulationMethod)
            {
                warnedMissingSimulationMethod = true;
                log?.Invoke("directKeyTimes warning: Simulated_PlayerControl_Update was not found; queued keys will wait for the normal update path.");
            }
            return;
        }

        try
        {
            forcingSimulation = true;
            simulatedPlayerControlUpdate.Invoke(controller, new object?[] { null });
        }
        catch (Exception ex)
        {
            log?.Invoke($"directKeyTimes forced Simulated_PlayerControl_Update failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            forcingSimulation = false;
        }
    }
}
