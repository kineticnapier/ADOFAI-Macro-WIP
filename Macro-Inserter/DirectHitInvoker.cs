using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Macro_Inserter;

internal sealed class DirectHitInvoker
{
    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private readonly object[] hitInvokeArgs = new object[1];
    private readonly Dictionary<Type, Func<object, int>?> floorSeqIdGetters = new();
    private object? controllerInstance;
    private object? conductorInstance;
    private MethodInfo? hitMethod;
    private Func<object, bool, bool>? hitDelegate;
    private Func<object, object?>? controllerCurrFloorGetter;
    private Func<object, int>? controllerFloorSeqIdGetter;

    public DirectHitInvoker(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
    }

    public void Warmup()
    {
        PrepareForRun();
        ReflectionCache.WarmupMembers("scrController", "currFloor", "currentFloor", "floor", "seqID", "currentFloorSeqID");
        ReflectionCache.WarmupMembers("scrConductor", "songposition", "songposition_minusi");
    }

    public bool PrepareForRun()
    {
        controllerInstance = ReflectionCache.GetSingletonInstance("scrController");
        conductorInstance = ReflectionCache.GetSingletonInstance("scrConductor");
        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        if (hitMethod != null && hitDelegate == null)
        {
            hitDelegate = TryBuildHitDelegate(hitMethod);
        }

        if (controllerInstance != null)
        {
            BuildControllerFloorGetters(controllerInstance.GetType());
        }

        return controllerInstance != null && hitMethod != null;
    }

    public bool CanUseFastPath()
    {
        if (controllerInstance == null || hitDelegate == null)
        {
            PrepareForRun();
        }

        return controllerInstance != null && hitDelegate != null;
    }

    public HitInvokeResult Invoke(int seqId, double audioTime, int beforeFloorSeqId, double targetTimeSeconds)
    {
        object? controller = controllerInstance ?? RefreshControllerInstance();
        if (controller == null)
        {
            LogNormal("scrController.instance was not found for DirectHit.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        if (hitMethod == null)
        {
            LogNormal("scrController.Hit(bool) was not found.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        object? result;
        TimeSpoofState? timeSpoofState = null;
        try
        {
            timeSpoofState = BeginTimeSpoof(targetTimeSeconds);
            result = InvokeHit(controller);
        }
        catch (Exception ex)
        {
            LogInvalidFloorIfNeeded(seqId, audioTime);
            LogNormal($"DirectHit threw {ex.GetType().Name}. seqID={seqId} audioTime={audioTime:F6}s.");
            return CreateResult(false, beforeFloorSeqId, ReadCurrentFloorSeqIdIfNeeded(), seqId);
        }
        finally
        {
            timeSpoofState?.Restore();
        }

        if (result is bool accepted)
        {
            LogVerbose($"DirectHit result={accepted} ignoreInput={settings.DirectHitIgnoreInput} seqID={seqId} audioTime={audioTime:F6}s");
            if (!accepted)
            {
                LogInvalidFloorIfNeeded(seqId, audioTime);
                LogNormal($"DirectHit failed. seqID={seqId} audioTime={audioTime:F6}s.");
            }

            if (!settings.ValidateAfterHit)
            {
                return new HitInvokeResult(
                    accepted,
                    false,
                    accepted,
                    accepted,
                    beforeFloorSeqId,
                    -1,
                    seqId);
            }

            return CreateResult(accepted, beforeFloorSeqId, ReadCurrentFloorSeqIdIfNeeded(), seqId);
        }

        return CreateResult(false, beforeFloorSeqId, ReadCurrentFloorSeqIdIfNeeded(), seqId);
    }

    public bool TryInvokeFast(double targetTimeSeconds, out bool accepted)
    {
        accepted = false;
        object? controller = controllerInstance;
        if (controller == null || hitDelegate == null)
        {
            if (!PrepareForRun())
            {
                return false;
            }

            controller = controllerInstance;
            if (controller == null || hitDelegate == null)
            {
                return false;
            }
        }

        TimeSpoofState? timeSpoofState = null;
        try
        {
            timeSpoofState = BeginTimeSpoof(targetTimeSeconds);
            accepted = hitDelegate(controller, settings.DirectHitIgnoreInput);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            timeSpoofState?.Restore();
        }
    }

    public bool TryReadCurrentFloorSeqId(out int seqId)
    {
        seqId = 0;
        object? controller = controllerInstance ?? RefreshControllerInstance();
        if (controller == null)
        {
            return false;
        }

        try
        {
            object? currFloor = controllerCurrFloorGetter?.Invoke(controller);
            if (currFloor != null)
            {
                if (currFloor is int intValue)
                {
                    seqId = intValue;
                    return true;
                }

                if (TryReadFloorSeqId(currFloor, out seqId))
                {
                    return true;
                }
            }

            if (controllerFloorSeqIdGetter != null)
            {
                seqId = controllerFloorSeqIdGetter(controller);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private object? RefreshControllerInstance()
    {
        controllerInstance = ReflectionCache.GetSingletonInstance("scrController");
        if (controllerInstance != null)
        {
            BuildControllerFloorGetters(controllerInstance.GetType());
        }

        return controllerInstance;
    }

    private object? InvokeHit(object controller)
    {
        if (hitDelegate != null)
        {
            return hitDelegate(controller, settings.DirectHitIgnoreInput);
        }

        if (hitMethod == null)
        {
            return false;
        }

        hitInvokeArgs[0] = settings.DirectHitIgnoreInput;
        return hitMethod.Invoke(controller, hitInvokeArgs);
    }

    private TimeSpoofState? BeginTimeSpoof(double targetTimeSeconds)
    {
        if (!settings.ExperimentalTimeSpoofForDirectHit)
        {
            return null;
        }

        try
        {
            object? conductor = conductorInstance ?? ReflectionCache.GetSingletonInstance("scrConductor");
            if (conductor == null)
            {
                LogNormal("timeSpoof failed: scrConductor.instance was not found.");
                return null;
            }

            conductorInstance = conductor;
            object? oldSongPosition = ReflectionCache.ReadMember(conductor, "songposition");
            object? oldSongPositionMinusI = ReflectionCache.ReadMember(conductor, "songposition_minusi");
            bool wroteSongPosition = ReflectionCache.WriteMember(conductor, targetTimeSeconds, "songposition");
            bool wroteSongPositionMinusI = ReflectionCache.WriteMember(conductor, targetTimeSeconds, "songposition_minusi");
            if (!wroteSongPosition && !wroteSongPositionMinusI)
            {
                LogNormal("timeSpoof failed: conductor songposition fields were not writable.");
                return null;
            }

            LogVerbose($"timeSpoof enabled targetTime={targetTimeSeconds:F6}s wroteSongposition={wroteSongPosition} wroteSongpositionMinusI={wroteSongPositionMinusI}");
            return new TimeSpoofState(
                conductor,
                oldSongPosition,
                oldSongPositionMinusI,
                wroteSongPosition,
                wroteSongPositionMinusI,
                message => LogVerbose(message));
        }
        catch (Exception ex)
        {
            LogNormal($"timeSpoof failed: {ex.GetType().Name}.");
            return null;
        }
    }

    private void LogInvalidFloorIfNeeded(int seqId, double audioTime)
    {
        if (seqId == 0)
        {
            LogNormal($"DirectHit failed because invalid floor 0 was scheduled. audioTime={audioTime:F6}s.");
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

    private int ReadCurrentFloorSeqIdIfNeeded()
    {
        return settings.ValidateAfterHit ? ReadCurrentFloorSeqId() : -1;
    }

    private int ReadCurrentFloorSeqId()
    {
        return TryReadCurrentFloorSeqId(out int seqId) ? seqId : -1;
    }

    private void BuildControllerFloorGetters(Type controllerType)
    {
        controllerCurrFloorGetter ??= TryBuildObjectGetter(controllerType, "currFloor", "currentFloor");
        controllerFloorSeqIdGetter ??= TryBuildIntGetter(controllerType, "floor", "seqID", "currentFloorSeqID");
    }

    private bool TryReadFloorSeqId(object floor, out int seqId)
    {
        seqId = 0;
        Type type = floor.GetType();
        if (!floorSeqIdGetters.TryGetValue(type, out Func<object, int>? getter))
        {
            getter = TryBuildIntGetter(type, "seqID", "seqId", "floorSeqID");
            floorSeqIdGetters[type] = getter;
        }

        if (getter == null)
        {
            return false;
        }

        seqId = getter(floor);
        return true;
    }

    private static Func<object, bool, bool>? TryBuildHitDelegate(MethodInfo method)
    {
        if (method.ReturnType != typeof(bool))
        {
            return null;
        }

        try
        {
            ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
            ParameterExpression ignoreInputParameter = Expression.Parameter(typeof(bool), "ignoreInput");
            Expression instance = method.IsStatic
                ? null!
                : Expression.Convert(targetParameter, method.DeclaringType!);
            MethodCallExpression call = method.IsStatic
                ? Expression.Call(method, ignoreInputParameter)
                : Expression.Call(instance, method, ignoreInputParameter);
            return Expression
                .Lambda<Func<object, bool, bool>>(call, targetParameter, ignoreInputParameter)
                .Compile();
        }
        catch
        {
            return null;
        }
    }

    private static Func<object, object?>? TryBuildObjectGetter(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            MemberInfo? member = FindReadableMember(type, name);
            if (member == null)
            {
                continue;
            }

            try
            {
                ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
                Expression instance = GetMemberInstance(targetParameter, member);
                Expression value = member is FieldInfo field
                    ? Expression.Field(instance, field)
                    : Expression.Property(instance, (PropertyInfo)member);
                Expression boxedValue = Expression.Convert(value, typeof(object));
                return Expression
                    .Lambda<Func<object, object?>>(boxedValue, targetParameter)
                    .Compile();
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private static Func<object, int>? TryBuildIntGetter(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            MemberInfo? member = FindReadableMember(type, name);
            if (member == null)
            {
                continue;
            }

            Type valueType = member is FieldInfo valueField ? valueField.FieldType : ((PropertyInfo)member).PropertyType;
            if (!CanConvertToInt(valueType))
            {
                continue;
            }

            try
            {
                ParameterExpression targetParameter = Expression.Parameter(typeof(object), "target");
                Expression instance = GetMemberInstance(targetParameter, member);
                Expression value = member is FieldInfo field
                    ? Expression.Field(instance, field)
                    : Expression.Property(instance, (PropertyInfo)member);
                Expression convertedValue = Expression.Convert(value, typeof(int));
                return Expression
                    .Lambda<Func<object, int>>(convertedValue, targetParameter)
                    .Compile();
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    private static MemberInfo? FindReadableMember(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo? field = type.GetField(name, flags);
        if (field != null)
        {
            return field;
        }

        PropertyInfo? property = type.GetProperty(name, flags);
        if (property == null || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return property.GetGetMethod(nonPublic: true) == null ? null : property;
    }

    private static Expression GetMemberInstance(ParameterExpression targetParameter, MemberInfo member)
    {
        Type? declaringType = member.DeclaringType;
        bool isStatic = member is FieldInfo field
            ? field.IsStatic
            : ((PropertyInfo)member).GetGetMethod(nonPublic: true)!.IsStatic;
        return isStatic
            ? null!
            : Expression.Convert(targetParameter, declaringType!);
    }

    private static bool CanConvertToInt(Type type)
    {
        Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsEnum ||
               underlyingType == typeof(byte) ||
               underlyingType == typeof(sbyte) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(ushort) ||
               underlyingType == typeof(int) ||
               underlyingType == typeof(uint) ||
               underlyingType == typeof(long) ||
               underlyingType == typeof(ulong);
    }

    private void LogNormal(string message)
    {
        if (settings.LoggingMode >= LoggingMode.Normal)
        {
            log(message);
        }
    }

    private void LogVerbose(string message)
    {
        if (settings.LoggingMode == LoggingMode.Verbose)
        {
            log(message);
        }
    }

    private sealed class TimeSpoofState
    {
        private readonly object conductor;
        private readonly object? oldSongPosition;
        private readonly object? oldSongPositionMinusI;
        private readonly bool restoreSongPosition;
        private readonly bool restoreSongPositionMinusI;
        private readonly Action<string> logVerbose;

        public TimeSpoofState(
            object conductor,
            object? oldSongPosition,
            object? oldSongPositionMinusI,
            bool restoreSongPosition,
            bool restoreSongPositionMinusI,
            Action<string> logVerbose)
        {
            this.conductor = conductor;
            this.oldSongPosition = oldSongPosition;
            this.oldSongPositionMinusI = oldSongPositionMinusI;
            this.restoreSongPosition = restoreSongPosition;
            this.restoreSongPositionMinusI = restoreSongPositionMinusI;
            this.logVerbose = logVerbose;
        }

        public void Restore()
        {
            bool restoredSongPosition = !restoreSongPosition ||
                                        ReflectionCache.WriteMember(conductor, oldSongPosition, "songposition");
            bool restoredSongPositionMinusI = !restoreSongPositionMinusI ||
                                              ReflectionCache.WriteMember(conductor, oldSongPositionMinusI, "songposition_minusi");
            if (!restoredSongPosition || !restoredSongPositionMinusI)
            {
                logVerbose($"timeSpoof failed to restore fully. restoredSongposition={restoredSongPosition} restoredSongpositionMinusI={restoredSongPositionMinusI}");
            }
        }
    }
}
