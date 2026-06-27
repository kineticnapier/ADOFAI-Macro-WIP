using System;
using System.Reflection;

namespace Macro_Inserter;

internal sealed class DirectHitInvoker
{
    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private MethodInfo? hitMethod;

    public DirectHitInvoker(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
    }

    public HitInvokeResult Invoke(int seqId, double audioTime, int beforeFloorSeqId)
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            log("scrController.instance was not found for DirectHit.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        if (hitMethod == null)
        {
            log("scrController.Hit(bool) was not found.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        object? result;
        try
        {
            result = hitMethod.Invoke(controller, new object[] { settings.DirectHitIgnoreInput });
        }
        catch (Exception ex)
        {
            LogInvalidFloorIfNeeded(seqId, audioTime);
            log($"DirectHit threw {ex.GetType().Name}. seqID={seqId} audioTime={audioTime:F6}s.");
            return CreateResult(false, beforeFloorSeqId, ReadCurrentFloorSeqId(), seqId);
        }

        if (result is bool accepted)
        {
            log($"DirectHit result={accepted} ignoreInput={settings.DirectHitIgnoreInput} seqID={seqId} audioTime={audioTime:F6}s");
            if (!accepted)
            {
                LogInvalidFloorIfNeeded(seqId, audioTime);
                log($"DirectHit failed. seqID={seqId} audioTime={audioTime:F6}s.");
            }

            return CreateResult(accepted, beforeFloorSeqId, ReadCurrentFloorSeqId(), seqId);
        }

        return CreateResult(false, beforeFloorSeqId, ReadCurrentFloorSeqId(), seqId);
    }

    private void LogInvalidFloorIfNeeded(int seqId, double audioTime)
    {
        if (seqId == 0)
        {
            log($"DirectHit failed because invalid floor 0 was scheduled. audioTime={audioTime:F6}s.");
        }
    }

    private static HitInvokeResult CreateResult(bool accepted, int beforeFloorSeqId, int afterFloorSeqId, int targetSeqId)
    {
        bool immediateAdvanced = afterFloorSeqId > beforeFloorSeqId;
        bool atOrPastTarget = afterFloorSeqId >= targetSeqId;
        bool shouldConsume = accepted && atOrPastTarget;
        return new HitInvokeResult(
            accepted,
            immediateAdvanced,
            atOrPastTarget,
            shouldConsume,
            beforeFloorSeqId,
            afterFloorSeqId,
            targetSeqId);
    }

    private static int ReadCurrentFloorSeqId()
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            return -1;
        }

        object? currFloor = ReflectionCache.ReadMember(controller, "currFloor", "currentFloor");
        if (currFloor == null)
        {
            return ReflectionCache.TryReadInt(controller, out int controllerSeqId, "floor", "seqID", "currentFloorSeqID")
                ? controllerSeqId
                : -1;
        }

        if (currFloor is int intValue)
        {
            return intValue;
        }

        return ReflectionCache.TryReadInt(currFloor, out int seqId, "seqID", "seqId", "floorSeqID")
            ? seqId
            : -1;
    }
}
