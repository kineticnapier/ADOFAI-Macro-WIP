using System;
using System.Reflection;

namespace Macro_Inserter;

internal static class RuntimeWarmup
{
    private static bool dotweenCapacityAttempted;

    public static void TrySetDotweenCapacity()
    {
        if (dotweenCapacityAttempted)
        {
            return;
        }

        dotweenCapacityAttempted = true;
        try
        {
            Type? dotween = ReflectionCache.FindType("DG.Tweening.DOTween");
            MethodInfo? setTweensCapacity = dotween?.GetMethod(
                "SetTweensCapacity",
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(int), typeof(int) },
                modifiers: null);
            setTweensCapacity?.Invoke(null, new object[] { 10000, 2000 });
        }
        catch
        {
        }
    }
}
