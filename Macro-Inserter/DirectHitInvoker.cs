using System;
using System.Reflection;

namespace Macro_Inserter;

internal sealed class DirectHitInvoker
{
    private readonly Action<string> log;
    private MethodInfo? hitMethod;

    public DirectHitInvoker(Action<string> log)
    {
        this.log = log;
    }

    public bool Invoke()
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            log("scrController.instance was not found for DirectHit.");
            return false;
        }

        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit");
        if (hitMethod == null)
        {
            log("scrController.Hit(bool) was not found.");
            return false;
        }

        hitMethod.Invoke(controller, new object[] { false });
        return true;
    }
}
