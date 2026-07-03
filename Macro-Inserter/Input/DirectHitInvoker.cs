using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Macro_Inserter;

internal sealed class DirectHitInvoker
{
    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private MethodInfo? hitMethod;
    private object? cachedConductor;
    private Type? cachedConductorType;
    private TimeSpoofMemberAccessor? songPositionAccessor;
    private TimeSpoofMemberAccessor? songPositionMinusIAccessor;

    public DirectHitInvoker(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
    }

    public void Warmup()
    {
        ReflectionCache.GetSingletonInstance("scrController");
        GetCachedConductor();
        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        ReflectionCache.WarmupMembers("scrController", "currFloor", "currentFloor", "floor", "seqID", "currentFloorSeqID");
        ReflectionCache.WarmupMembers("scrConductor", "songposition", "songposition_minusi");
        WarmupTimeSpoofAccessors();
    }

    public HitInvokeResult Invoke(
        int seqId,
        double audioTime,
        int beforeFloorSeqId,
        double targetTimeSeconds,
        bool? ignoreInputOverride = null)
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            LogNormal("scrController.instance was not found for DirectHit.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        if (hitMethod == null)
        {
            LogNormal("scrController.Hit(bool) was not found.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        object? result;
        TimeSpoofState? timeSpoofState = null;
        bool ignoreInput = ignoreInputOverride ?? settings.DirectHitIgnoreInput;
        try
        {
            timeSpoofState = BeginTimeSpoof(targetTimeSeconds);
            result = hitMethod.Invoke(controller, new object[] { ignoreInput });
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
            LogVerbose($"DirectHit result={accepted} ignoreInput={ignoreInput} seqID={seqId} audioTime={audioTime:F6}s");
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

    private TimeSpoofState? BeginTimeSpoof(double targetTimeSeconds)
    {
        if (!settings.ExperimentalTimeSpoofForDirectHit)
        {
            return null;
        }

        try
        {
            object? conductor = GetCachedConductor();
            if (conductor == null)
            {
                LogNormal("timeSpoof failed: scrConductor.instance was not found.");
                return null;
            }

            cachedConductor = conductor;
            if (!EnsureTimeSpoofAccessors(conductor.GetType()))
            {
                LogNormal("timeSpoof failed: conductor songposition fields were not writable.");
                return null;
            }

            if (!songPositionAccessor!.TryGet(conductor, out object? oldSongPosition) ||
                !songPositionMinusIAccessor!.TryGet(conductor, out object? oldSongPositionMinusI))
            {
                LogNormal("timeSpoof failed: conductor songposition fields were not readable.");
                return null;
            }

            if (!songPositionAccessor.TrySet(conductor, targetTimeSeconds))
            {
                LogNormal("timeSpoof failed: conductor songposition field was not writable.");
                return null;
            }

            if (!songPositionMinusIAccessor.TrySet(conductor, targetTimeSeconds))
            {
                songPositionAccessor.TrySet(conductor, oldSongPosition);
                LogNormal("timeSpoof failed: conductor songposition_minusi field was not writable.");
                return null;
            }

            LogVerbose($"timeSpoof enabled targetTime={targetTimeSeconds:F6}s");
            return new TimeSpoofState(
                conductor,
                songPositionAccessor,
                songPositionMinusIAccessor,
                oldSongPosition,
                oldSongPositionMinusI,
                message => LogVerbose(message));
        }
        catch (Exception ex)
        {
            LogNormal($"timeSpoof failed: {ex.GetType().Name}.");
            return null;
        }
    }

    private object? GetCachedConductor()
    {
        if (cachedConductor is UnityEngine.Object unityObject && unityObject == null)
        {
            cachedConductor = null;
        }

        cachedConductor ??= ReflectionCache.GetSingletonInstance("scrConductor");
        return cachedConductor;
    }

    private void WarmupTimeSpoofAccessors()
    {
        object? conductor = cachedConductor;
        if (conductor == null)
        {
            return;
        }

        EnsureTimeSpoofAccessors(conductor.GetType());
    }

    private bool EnsureTimeSpoofAccessors(Type conductorType)
    {
        if (cachedConductorType == conductorType &&
            songPositionAccessor != null &&
            songPositionMinusIAccessor != null)
        {
            return true;
        }

        cachedConductorType = conductorType;
        songPositionAccessor = TimeSpoofMemberAccessor.Create(conductorType, "songposition");
        songPositionMinusIAccessor = TimeSpoofMemberAccessor.Create(conductorType, "songposition_minusi");
        return songPositionAccessor != null && songPositionMinusIAccessor != null;
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
        private readonly TimeSpoofMemberAccessor songPositionAccessor;
        private readonly TimeSpoofMemberAccessor songPositionMinusIAccessor;
        private readonly object? oldSongPosition;
        private readonly object? oldSongPositionMinusI;
        private readonly Action<string> logVerbose;

        public TimeSpoofState(
            object conductor,
            TimeSpoofMemberAccessor songPositionAccessor,
            TimeSpoofMemberAccessor songPositionMinusIAccessor,
            object? oldSongPosition,
            object? oldSongPositionMinusI,
            Action<string> logVerbose)
        {
            this.conductor = conductor;
            this.songPositionAccessor = songPositionAccessor;
            this.songPositionMinusIAccessor = songPositionMinusIAccessor;
            this.oldSongPosition = oldSongPosition;
            this.oldSongPositionMinusI = oldSongPositionMinusI;
            this.logVerbose = logVerbose;
        }

        public void Restore()
        {
            bool restoredSongPosition = songPositionAccessor.TrySet(conductor, oldSongPosition);
            bool restoredSongPositionMinusI = songPositionMinusIAccessor.TrySet(conductor, oldSongPositionMinusI);
            if (!restoredSongPosition || !restoredSongPositionMinusI)
            {
                logVerbose($"timeSpoof failed to restore fully. restoredSongposition={restoredSongPosition} restoredSongpositionMinusI={restoredSongPositionMinusI}");
            }
        }
    }

    private sealed class TimeSpoofMemberAccessor
    {
        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Type valueType;
        private readonly Func<object, object?> getter;
        private readonly Action<object, object?> setter;

        private TimeSpoofMemberAccessor(
            Type valueType,
            Func<object, object?> getter,
            Action<object, object?> setter)
        {
            this.valueType = valueType;
            this.getter = getter;
            this.setter = setter;
        }

        public static TimeSpoofMemberAccessor? Create(Type ownerType, string memberName)
        {
            FieldInfo? field = ownerType.GetField(memberName, InstanceFlags);
            if (field != null)
            {
                if (field.IsInitOnly || field.IsLiteral)
                {
                    return null;
                }

                Func<object, object?> getter = CreateFieldGetter(field) ?? (instance => field.GetValue(instance));
                Action<object, object?> setter = CreateFieldSetter(field) ?? ((instance, value) => field.SetValue(instance, CoerceValue(value, field.FieldType)));
                return new TimeSpoofMemberAccessor(field.FieldType, getter, setter);
            }

            PropertyInfo? property = ownerType.GetProperty(memberName, InstanceFlags);
            if (property == null ||
                property.GetIndexParameters().Length != 0 ||
                property.GetGetMethod(nonPublic: true) == null ||
                property.GetSetMethod(nonPublic: true) == null)
            {
                return null;
            }

            Func<object, object?> propertyGetter = CreatePropertyGetter(property) ??
                                                  (instance => property.GetValue(instance, null));
            Action<object, object?> propertySetter = CreatePropertySetter(property) ??
                                                     ((instance, value) => property.SetValue(instance, CoerceValue(value, property.PropertyType), null));
            return new TimeSpoofMemberAccessor(property.PropertyType, propertyGetter, propertySetter);
        }

        public bool TryGet(object instance, out object? value)
        {
            try
            {
                value = getter(instance);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public bool TrySet(object instance, object? value)
        {
            try
            {
                setter(instance, CoerceValue(value, valueType));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Func<object, object?>? CreateFieldGetter(FieldInfo field)
        {
            try
            {
                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                UnaryExpression typedInstance = Expression.Convert(instance, field.DeclaringType!);
                MemberExpression fieldAccess = Expression.Field(typedInstance, field);
                UnaryExpression boxedValue = Expression.Convert(fieldAccess, typeof(object));
                return Expression.Lambda<Func<object, object?>>(boxedValue, instance).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Action<object, object?>? CreateFieldSetter(FieldInfo field)
        {
            try
            {
                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                ParameterExpression value = Expression.Parameter(typeof(object), "value");
                UnaryExpression typedInstance = Expression.Convert(instance, field.DeclaringType!);
                UnaryExpression typedValue = Expression.Convert(value, field.FieldType);
                BinaryExpression assign = Expression.Assign(Expression.Field(typedInstance, field), typedValue);
                return Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Func<object, object?>? CreatePropertyGetter(PropertyInfo property)
        {
            try
            {
                MethodInfo? getter = property.GetGetMethod(nonPublic: true);
                if (getter == null)
                {
                    return null;
                }

                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                UnaryExpression typedInstance = Expression.Convert(instance, property.DeclaringType!);
                MethodCallExpression propertyAccess = Expression.Call(typedInstance, getter);
                UnaryExpression boxedValue = Expression.Convert(propertyAccess, typeof(object));
                return Expression.Lambda<Func<object, object?>>(boxedValue, instance).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Action<object, object?>? CreatePropertySetter(PropertyInfo property)
        {
            try
            {
                MethodInfo? setter = property.GetSetMethod(nonPublic: true);
                if (setter == null)
                {
                    return null;
                }

                ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
                ParameterExpression value = Expression.Parameter(typeof(object), "value");
                UnaryExpression typedInstance = Expression.Convert(instance, property.DeclaringType!);
                UnaryExpression typedValue = Expression.Convert(value, property.PropertyType);
                MethodCallExpression assign = Expression.Call(typedInstance, setter, typedValue);
                return Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static object? CoerceValue(object? value, Type destinationType)
        {
            if (value == null)
            {
                return destinationType.IsValueType ? Activator.CreateInstance(destinationType) : null;
            }

            Type valueType = value.GetType();
            if (destinationType.IsAssignableFrom(valueType))
            {
                return value;
            }

            if (destinationType.IsEnum)
            {
                return Enum.ToObject(destinationType, value);
            }

            return Convert.ChangeType(value, destinationType);
        }
    }
}
