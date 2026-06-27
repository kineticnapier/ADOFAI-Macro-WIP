using System;
using System.Reflection;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class HitInputEventInvoker
{
    private const bool IsAuto = false;

    private static object? lastCapturedInputEventState;
    private static Type? lastCapturedInputEventStateType;
    private static bool macroInvokeInProgress;

    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private Type? inputEventStateType;
    private MethodInfo? hitInputEventMethod;

    public HitInputEventInvoker(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
    }

    public static void CaptureHumanInputEventState(bool isAuto, object? inputEventState)
    {
        if (macroInvokeInProgress || isAuto || inputEventState == null)
        {
            return;
        }

        lastCapturedInputEventStateType = inputEventState.GetType();
        lastCapturedInputEventState = CloneObject(lastCapturedInputEventStateType, inputEventState) ?? inputEventState;
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

        object? chosenPlanet = ReadChosenPlanet(controller);
        object? controllerCurrFloor = ReadControllerCurrFloor(controller);
        object? planetCurrFloor = ReadPlanetCurrFloor(chosenPlanet);
        object? planetNextFloor = ReadNextFloor(planetCurrFloor);
        object? nextFloor = planetNextFloor ?? ReflectionCache.ReadMember(controller, "nextfloor", "nextFloor");
        int controllerCurrSeqId = ReadFloorSeqId(controllerCurrFloor);
        int beforePlanetFloorSeq = ReadFloorSeqId(planetCurrFloor);
        int beforePlanetNextFloorSeq = ReadFloorSeqId(planetNextFloor);
        int nextSeqId = ReadFloorSeqId(nextFloor);

        log($"HitInputEvent before targetSeqID={seqId} controllerCurrFloorSeqID={controllerCurrSeqId} chosenPlanetCurrFloorSeqID={beforePlanetFloorSeq} chosenPlanetNextFloorSeqID={beforePlanetNextFloorSeq} resolvedNextFloorSeqID={nextSeqId} audioTime={audioTime:F6}s stateMode={settings.StateMode}");

        CorrectCurrentState(controller, chosenPlanet, controllerCurrFloor, nextFloor);

        object? inputEventState = CreateInputEventState();
        object? result;
        try
        {
            macroInvokeInProgress = true;
            result = hitInputEventMethod!.Invoke(controller, new[] { (object)IsAuto, inputEventState });
        }
        catch (Exception ex)
        {
            Exception root = ex.InnerException ?? ex;
            log($"HitInputEvent threw {ex.GetType().Name}: {root.GetType().Name}: {root.Message}. seqID={seqId} controllerCurrFloorSeqID={controllerCurrSeqId} chosenPlanetCurrFloorSeqID={beforePlanetFloorSeq} chosenPlanetNextFloorSeqID={beforePlanetNextFloorSeq} audioTime={audioTime:F6}s");
            return false;
        }
        finally
        {
            macroInvokeInProgress = false;
        }

        object? afterPlanetCurrFloor = ReadPlanetCurrFloor(ReadChosenPlanet(controller));
        int afterPlanetFloorSeq = ReadFloorSeqId(afterPlanetCurrFloor);

        if (result is bool accepted)
        {
            log($"HitInputEvent after targetSeqID={seqId} result={accepted} beforePlanetFloorSeq={beforePlanetFloorSeq} afterPlanetFloorSeq={afterPlanetFloorSeq} audioTime={audioTime:F6}s");
            if (accepted && afterPlanetFloorSeq != beforePlanetFloorSeq + 1)
            {
                log($"HitInputEvent returned true but floor did not advance. targetSeqID={seqId} beforePlanetFloorSeq={beforePlanetFloorSeq} afterPlanetFloorSeq={afterPlanetFloorSeq}");
            }

            return accepted;
        }

        log($"HitInputEvent returned non-bool result. seqID={seqId} controllerCurrFloorSeqID={controllerCurrSeqId} chosenPlanetCurrFloorSeqID={beforePlanetFloorSeq} chosenPlanetNextFloorSeqID={beforePlanetNextFloorSeq} audioTime={audioTime:F6}s");
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

    private object? CreateInputEventState()
    {
        if (settings.StateMode == StateMode.CapturedHumanState &&
            lastCapturedInputEventState != null &&
            lastCapturedInputEventStateType == inputEventStateType)
        {
            object? clone = CloneInputEventState(lastCapturedInputEventState);
            if (clone != null)
            {
                return clone;
            }
        }

        return CreateDefaultInputEventState();
    }

    private object? CreateDefaultInputEventState()
    {
        if (inputEventStateType == null || !inputEventStateType.IsValueType)
        {
            return null;
        }

        return Activator.CreateInstance(inputEventStateType);
    }

    private object? CloneInputEventState(object source)
    {
        if (inputEventStateType == null)
        {
            return null;
        }

        return CloneObject(inputEventStateType, source);
    }

    private static object? CloneObject(Type type, object source)
    {
        if (type.IsValueType)
        {
            return source;
        }

        object? clone;
        try
        {
            clone = Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }

        if (clone == null)
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in type.GetFields(flags))
        {
            if (field.IsInitOnly || field.IsLiteral)
            {
                continue;
            }

            try
            {
                field.SetValue(clone, field.GetValue(source));
            }
            catch
            {
                continue;
            }
        }

        foreach (PropertyInfo property in type.GetProperties(flags))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                property.SetValue(clone, property.GetValue(source, null), null);
            }
            catch
            {
                continue;
            }
        }

        return clone;
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

    private static object? ReadChosenPlanet(object controller)
    {
        return ReflectionCache.ReadMember(controller, "chosenPlanet", "selectedPlanet");
    }

    private static object? ReadControllerCurrFloor(object controller)
    {
        return ReflectionCache.ReadMember(controller, "currFloor", "currentFloor");
    }

    private static object? ReadPlanetCurrFloor(object? chosenPlanet)
    {
        return chosenPlanet == null
            ? null
            : ReflectionCache.ReadMember(chosenPlanet, "currfloor", "currFloor", "currentFloor");
    }

    private static object? ReadNextFloor(object? floor)
    {
        return floor == null
            ? null
            : ReflectionCache.ReadMember(floor, "nextfloor", "nextFloor", "next");
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
