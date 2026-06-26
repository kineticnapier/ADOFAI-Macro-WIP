using System;
using System.Collections;
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

    public static Type? FindType(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(typeName, throwOnError: false);
            if (type != null)
            {
                return type;
            }

            type = GetLoadableTypes(assembly).FirstOrDefault(candidate => candidate.Name == typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type != null).Cast<Type>().ToArray();
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
        return FindType(typeName)?.GetMethod(methodName, InstanceFlags | StaticFlags);
    }

    public static object? ReadMember(object instance, params string[] names)
    {
        return ReadMember(instance.GetType(), instance, names);
    }

    public static object? ReadMember(Type type, object? instance, params string[] names)
    {
        foreach (string name in names)
        {
            FieldInfo? field = type.GetField(name, InstanceFlags | StaticFlags);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            PropertyInfo? property = type.GetProperty(name, InstanceFlags | StaticFlags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                return property.GetValue(instance, null);
            }
        }

        return null;
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

    public static AudioSource? FindAudioSource(object controller)
    {
        Type type = controller.GetType();

        foreach (FieldInfo field in type.GetFields(InstanceFlags))
        {
            if (typeof(AudioSource).IsAssignableFrom(field.FieldType) &&
                field.GetValue(controller) is AudioSource source &&
                source.clip != null)
            {
                return source;
            }
        }

        foreach (PropertyInfo property in type.GetProperties(InstanceFlags))
        {
            if (!typeof(AudioSource).IsAssignableFrom(property.PropertyType) ||
                property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            if (property.GetValue(controller, null) is AudioSource source && source.clip != null)
            {
                return source;
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
