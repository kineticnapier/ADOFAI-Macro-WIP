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

    public bool Invoke(int seqId, double audioTime)
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            log("scrController.instance was not found for DirectHit.");
            return false;
        }

        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        if (hitMethod == null)
        {
            log("scrController.Hit(bool) was not found.");
            return false;
        }

        object? result;
        try
        {
            result = hitMethod.Invoke(controller, new object[] { settings.DirectHitIgnoreInput });
        }
        catch (Exception ex)
        {
            LogInvalidFloorIfNeeded(seqId, audioTime);
            log($"DirectHit threw {ex.GetType().Name}. seqID={seqId} audioTime={audioTime:F6}s. DirectHit is experimental; HitInputEvent mode is recommended.");
            return false;
        }

        if (result is bool accepted)
        {
            log($"DirectHit result={accepted} ignoreInput={settings.DirectHitIgnoreInput} seqID={seqId} audioTime={audioTime:F6}s");
            if (!accepted)
            {
                LogInvalidFloorIfNeeded(seqId, audioTime);
                log($"DirectHit failed. seqID={seqId} audioTime={audioTime:F6}s. DirectHit is experimental; HitInputEvent mode is recommended.");
            }

            return accepted;
        }

        return true;
    }

    private void LogInvalidFloorIfNeeded(int seqId, double audioTime)
    {
        if (seqId == 0)
        {
            log($"DirectHit failed because invalid floor 0 was scheduled. audioTime={audioTime:F6}s.");
        }
    }
}
