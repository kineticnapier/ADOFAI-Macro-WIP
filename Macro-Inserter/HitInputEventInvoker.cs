using System;
using System.Reflection;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class HitInputEventInvoker
{
    private const bool IsAuto = false;

    private readonly Action<string> log;
    private Type? inputEventStateType;
    private MethodInfo? hitInputEventMethod;

    public HitInputEventInvoker(Action<string> log)
    {
        this.log = log;
    }

    public bool Invoke(int seqId, double audioTime)
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            log("scrController.instance was not found for HitInputEvent.");
            return false;
        }

        if (!EnsureReflectionReady())
        {
            return false;
        }

        object? chosenPlanet = ReflectionCache.ReadMember(controller, "chosenPlanet", "selectedPlanet");
        object? currFloor = ReflectionCache.ReadMember(controller, "currFloor", "currentFloor");
        object? nextFloor = ReflectionCache.ReadMember(controller, "nextfloor", "nextFloor");
        int currSeqId = ReadFloorSeqId(currFloor);
        int nextSeqId = ReadFloorSeqId(nextFloor);

        CorrectCurrentState(controller, chosenPlanet, currFloor, nextFloor);

        object? inputEventState = CreateDefaultInputEventState();
        object? result;
        try
        {
            result = hitInputEventMethod!.Invoke(controller, new[] { (object)IsAuto, inputEventState });
        }
        catch (Exception ex)
        {
            Exception root = ex.InnerException ?? ex;
            log($"HitInputEvent threw {ex.GetType().Name}: {root.GetType().Name}: {root.Message}. seqID={seqId} currFloorSeqID={currSeqId} nextFloorSeqID={nextSeqId} audioTime={audioTime:F6}s");
            return false;
        }

        if (result is bool accepted)
        {
            log($"HitInputEvent result={accepted} isAuto={IsAuto} seqID={seqId} currFloorSeqID={currSeqId} nextFloorSeqID={nextSeqId} audioTime={audioTime:F6}s");
            return accepted;
        }

        log($"HitInputEvent returned non-bool result. seqID={seqId} currFloorSeqID={currSeqId} nextFloorSeqID={nextSeqId} audioTime={audioTime:F6}s");
        return true;
    }

    private bool EnsureReflectionReady()
    {
        inputEventStateType ??= ReflectionCache.FindType("InputEventState");
        if (inputEventStateType == null)
        {
            log("InputEventState type was not found for HitInputEvent.");
            return false;
        }

        hitInputEventMethod ??= ReflectionCache.FindMethod("scrController", "HitInputEvent", typeof(bool), inputEventStateType);
        if (hitInputEventMethod == null)
        {
            log("scrController.HitInputEvent(bool, InputEventState) was not found.");
            return false;
        }

        return true;
    }

    private object? CreateDefaultInputEventState()
    {
        if (inputEventStateType == null || !inputEventStateType.IsValueType)
        {
            return null;
        }

        return Activator.CreateInstance(inputEventStateType);
    }

    private void CorrectCurrentState(object controller, object? chosenPlanet, object? currFloor, object? nextFloor)
    {
        CorrectCachedAngle(controller, chosenPlanet);
        SyncMemberFromCurrentState(controller, "targetExitAngle", nextFloor, currFloor);
        SyncMemberFromCurrentState(controller, "midspinInfiniteMargin", nextFloor, currFloor);
        SyncMemberFromCurrentState(controller, "responsive", nextFloor, currFloor);

        if (chosenPlanet != null)
        {
            SyncMemberFromCurrentState(chosenPlanet, "targetExitAngle", nextFloor, currFloor);
            SyncMemberFromCurrentState(chosenPlanet, "midspinInfiniteMargin", nextFloor, currFloor);
            SyncMemberFromCurrentState(chosenPlanet, "responsive", nextFloor, currFloor);
        }
    }

    private static void CorrectCachedAngle(object controller, object? chosenPlanet)
    {
        if (chosenPlanet == null)
        {
            return;
        }

        object? transformValue = ReflectionCache.ReadMember(chosenPlanet, "transform");
        if (transformValue is not Transform transform)
        {
            return;
        }

        ReflectionCache.WriteMember(controller, transform.eulerAngles.z, "cachedAngle");
        ReflectionCache.WriteMember(chosenPlanet, transform.eulerAngles.z, "cachedAngle");
    }

    private static void SyncMemberFromCurrentState(object target, string memberName, params object?[] sources)
    {
        foreach (object? source in sources)
        {
            if (source == null)
            {
                continue;
            }

            object? value = ReflectionCache.ReadMember(source, memberName);
            if (value == null)
            {
                continue;
            }

            ReflectionCache.WriteMember(target, value, memberName);
            return;
        }
    }

    private static int ReadFloorSeqId(object? floor)
    {
        if (floor == null)
        {
            return -1;
        }

        if (floor is int intValue)
        {
            return intValue;
        }

        return ReflectionCache.TryReadInt(floor, out int seqId, "seqID", "seqId", "floorSeqID")
            ? seqId
            : -1;
    }
}
