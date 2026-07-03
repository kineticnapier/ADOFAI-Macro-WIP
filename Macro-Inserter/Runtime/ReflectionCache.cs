using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Macro_Inserter;

internal static class ReflectionCache
{
    private static readonly BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<string, Type?> TypeCache = new();
    private static readonly Dictionary<Assembly, Type[]> AssemblyTypesCache = new();
    private static readonly Dictionary<string, MethodInfo?> MethodCache = new();
    private static readonly Dictionary<string, IReadOnlyList<MethodInfo>> MethodsCache = new();
    private static readonly Dictionary<string, MemberInfo?> ReadMemberCache = new();
    private static readonly Dictionary<string, MemberInfo?> WriteMemberCache = new();

    public static Type? FindType(string typeName)
    {
        if (TypeCache.TryGetValue(typeName, out Type? cachedType))
        {
            return cachedType;
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(typeName, throwOnError: false);
            if (type != null)
            {
                TypeCache[typeName] = type;
                return type;
            }

            type = GetLoadableTypes(assembly).FirstOrDefault(candidate => candidate.Name == typeName);
            if (type != null)
            {
                TypeCache[typeName] = type;
                return type;
            }
        }

        return null;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        if (AssemblyTypesCache.TryGetValue(assembly, out Type[] cachedTypes))
        {
            return cachedTypes;
        }

        try
        {
            Type[] types = assembly.GetTypes();
            AssemblyTypesCache[assembly] = types;
            return types;
        }
        catch (ReflectionTypeLoadException ex)
        {
            Type[] types = ex.Types.Where(type => type != null).Cast<Type>().ToArray();
            AssemblyTypesCache[assembly] = types;
            return types;
        }
    }

    public static object? GetSingletonInstance(string typeName)
    {
        Type? type = FindType(typeName);
        if (type == null)
        {
            return null;
        }

        return ReadMember(type, instance: null, names: new[] { "instance", "Instance", "inst" });
    }

    public static MethodInfo? FindMethod(string typeName, string methodName)
    {
        string key = $"{typeName}.{methodName}";
        if (MethodCache.TryGetValue(key, out MethodInfo? cachedMethod))
        {
            return cachedMethod;
        }

        Type? type = FindType(typeName);
        if (type == null)
        {
            return null;
        }

        try
        {
            MethodInfo? method = type.GetMethod(methodName, InstanceFlags | StaticFlags);
            MethodCache[key] = method;
            return method;
        }
        catch (AmbiguousMatchException)
        {
            MethodInfo? method = type
                .GetMethods(InstanceFlags | StaticFlags)
                .FirstOrDefault(method => method.Name == methodName);
            MethodCache[key] = method;
            return method;
        }
    }

    public static MethodInfo? FindMethod(string typeName, string methodName, params Type[] parameterTypes)
    {
        string key = $"{typeName}.{methodName}({string.Join(",", parameterTypes.Select(type => type.FullName).ToArray())})";
        if (MethodCache.TryGetValue(key, out MethodInfo? cachedMethod))
        {
            return cachedMethod;
        }

        Type? type = FindType(typeName);
        if (type == null)
        {
            return null;
        }

        MethodInfo? method = type.GetMethod(
            methodName,
            InstanceFlags | StaticFlags,
            binder: null,
            types: parameterTypes,
            modifiers: null);
        MethodCache[key] = method;
        return method;
    }

    public static IReadOnlyList<MethodInfo> FindMethods(string typeName, string methodName)
    {
        string key = $"{typeName}.{methodName}:all";
        if (MethodsCache.TryGetValue(key, out IReadOnlyList<MethodInfo> cachedMethods))
        {
            return cachedMethods;
        }

        Type? type = FindType(typeName);
        if (type == null)
        {
            return Array.Empty<MethodInfo>();
        }

        IReadOnlyList<MethodInfo> methods = type
            .GetMethods(InstanceFlags | StaticFlags)
            .Where(method => method.Name == methodName)
            .ToArray();
        MethodsCache[key] = methods;
        return methods;
    }

    public static IReadOnlyList<MethodInfo> FindGameMethodsByName(string methodName)
    {
        List<MethodInfo> methods = new();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (Type type in GetLoadableTypes(assembly))
            {
                if (!type.Name.StartsWith("scr", StringComparison.Ordinal) &&
                    !type.Name.StartsWith("scn", StringComparison.Ordinal))
                {
                    continue;
                }

                methods.AddRange(type
                    .GetMethods(InstanceFlags | StaticFlags)
                    .Where(method => method.Name == methodName));
            }
        }

        return methods.ToArray();
    }

    public static object? ReadMember(object instance, params string[] names)
    {
        return ReadMember(instance.GetType(), instance, names);
    }

    public static object? ReadMember(Type type, object? instance, params string[] names)
    {
        foreach (string name in names)
        {
            MemberInfo? member = ResolveReadableMember(type, name);
            if (member is FieldInfo field)
            {
                if (instance == null && !field.IsStatic)
                {
                    continue;
                }

                try
                {
                    return field.GetValue(field.IsStatic ? null : instance);
                }
                catch
                {
                    continue;
                }
            }

            if (member is PropertyInfo property)
            {
                MethodInfo? getter = property.GetGetMethod(nonPublic: true);
                if (getter == null)
                {
                    continue;
                }

                if (instance == null && !getter.IsStatic)
                {
                    continue;
                }

                try
                {
                    return property.GetValue(getter.IsStatic ? null : instance, null);
                }
                catch
                {
                    continue;
                }
            }
        }

        return null;
    }

    public static bool WriteMember(object instance, object? value, params string[] names)
    {
        Type type = instance.GetType();
        foreach (string name in names)
        {
            MemberInfo? member = ResolveWritableMember(type, name);
            if (member is FieldInfo field)
            {
                if (field.IsInitOnly || field.IsLiteral)
                {
                    continue;
                }

                try
                {
                    field.SetValue(field.IsStatic ? null : instance, CoerceValue(value, field.FieldType));
                    return true;
                }
                catch
                {
                    continue;
                }
            }

            if (member is PropertyInfo property)
            {
                MethodInfo? setter = property.GetSetMethod(nonPublic: true);
                if (setter == null)
                {
                    continue;
                }

                try
                {
                    property.SetValue(setter.IsStatic ? null : instance, CoerceValue(value, property.PropertyType), null);
                    return true;
                }
                catch
                {
                    continue;
                }
            }
        }

        return false;
    }

    public static void WarmupMembers(string typeName, params string[] names)
    {
        Type? type = FindType(typeName);
        if (type == null)
        {
            return;
        }

        foreach (string name in names)
        {
            ResolveReadableMember(type, name);
            ResolveWritableMember(type, name);
        }
    }

    private static MemberInfo? ResolveReadableMember(Type type, string name)
    {
        string key = $"{type.FullName}:read:{name}";
        if (ReadMemberCache.TryGetValue(key, out MemberInfo? cachedMember))
        {
            return cachedMember;
        }

        FieldInfo? field = type.GetField(name, InstanceFlags | StaticFlags);
        if (field != null)
        {
            ReadMemberCache[key] = field;
            return field;
        }

        PropertyInfo? property = type.GetProperty(name, InstanceFlags | StaticFlags);
        if (property != null && property.GetIndexParameters().Length == 0)
        {
            ReadMemberCache[key] = property;
            return property;
        }

        ReadMemberCache[key] = null;
        return null;
    }

    private static MemberInfo? ResolveWritableMember(Type type, string name)
    {
        string key = $"{type.FullName}:write:{name}";
        if (WriteMemberCache.TryGetValue(key, out MemberInfo? cachedMember))
        {
            return cachedMember;
        }

        FieldInfo? field = type.GetField(name, InstanceFlags | StaticFlags);
        if (field != null && !field.IsInitOnly && !field.IsLiteral)
        {
            WriteMemberCache[key] = field;
            return field;
        }

        PropertyInfo? property = type.GetProperty(name, InstanceFlags | StaticFlags);
        if (property != null &&
            property.CanWrite &&
            property.GetIndexParameters().Length == 0)
        {
            WriteMemberCache[key] = property;
            return property;
        }

        WriteMemberCache[key] = null;
        return null;
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

    public static bool TryReadBool(string typeName, out bool value, params string[] names)
    {
        value = false;
        Type? type = FindType(typeName);
        if (type == null)
        {
            return false;
        }

        object? raw = ReadMember(type, null, names);
        if (raw is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        return false;
    }

    public static bool TryReadBool(object instance, out bool value, params string[] names)
    {
        value = false;
        object? raw = ReadMember(instance, names);
        if (raw is bool boolValue)
        {
            value = boolValue;
            return true;
        }

        return false;
    }

    public static bool TryReadInt(object instance, out int value, params string[] names)
    {
        value = 0;
        object? raw = ReadMember(instance, names);
        if (raw == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryReadDouble(object instance, out double value, params string[] names)
    {
        value = 0.0;
        object? raw = ReadMember(instance, names);
        if (raw == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToDouble(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static AudioSource? FindAudioSource(object controller)
    {
        Type type = controller.GetType();

        foreach (FieldInfo field in type.GetFields(InstanceFlags))
        {
            try
            {
                if (typeof(AudioSource).IsAssignableFrom(field.FieldType) &&
                    field.GetValue(controller) is AudioSource source &&
                    source.clip != null)
                {
                    return source;
                }
            }
            catch
            {
                continue;
            }
        }

        foreach (PropertyInfo property in type.GetProperties(InstanceFlags))
        {
            if (!typeof(AudioSource).IsAssignableFrom(property.PropertyType) ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            try
            {
                if (property.GetValue(controller, null) is AudioSource source && source.clip != null)
                {
                    return source;
                }
            }
            catch
            {
                continue;
            }
        }

        AudioSource[] sources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
        return sources.FirstOrDefault(source => source != null && source.clip != null && source.isPlaying);
    }

    public static IEnumerable AsEnumerable(object? value)
    {
        return value as IEnumerable ?? Array.Empty<object>();
    }
}
