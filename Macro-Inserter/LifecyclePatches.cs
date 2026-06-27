using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Macro_Inserter;

internal static class LifecyclePatches
{
    private static readonly string[] StopMethodNames =
    {
        "SwitchToEditMode",
        "Restart",
        "QuitToMainMenu",
        "Won_Update"
    };

    private static Func<InternalMacroService?>? getService;
    private static Func<InternalMacroSettings>? getSettings;

    public static void Apply(
        Harmony harmony,
        Action<string> log,
        Func<InternalMacroService?> serviceAccessor,
        Func<InternalMacroSettings> settingsAccessor)
    {
        getService = serviceAccessor;
        getSettings = settingsAccessor;

        PatchStartRewind(harmony, log);
        foreach (string methodName in StopMethodNames)
        {
            PatchStopMethod(harmony, log, methodName, nameof(StopSchedulerPrefix));
        }

        if (getSettings().EnableFail2ActionStopPatch)
        {
            PatchStopMethod(harmony, log, "Fail2Action", nameof(Fail2ActionStopSchedulerPrefix));
        }
        else
        {
            log("Patch skipped: Fail2Action lifecycle stop is disabled.");
        }
    }

    private static void PatchStartRewind(Harmony harmony, Action<string> log)
    {
        IReadOnlyList<MethodInfo> targets = ReflectionCache.FindMethods("scrController", "Start_Rewind");
        MethodInfo? postfix = typeof(LifecyclePatches).GetMethod(nameof(StartRewindPostfix), BindingFlags.Static | BindingFlags.NonPublic);
        if (targets.Count == 0 || postfix == null)
        {
            log("Patch skipped: scrController.Start_Rewind was not found.");
            return;
        }

        foreach (MethodInfo target in targets)
        {
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        log($"Patch applied: scrController.Start_Rewind postfix ({targets.Count} overloads)");
    }

    private static void PatchStopMethod(Harmony harmony, Action<string> log, string methodName, string patchMethodName)
    {
        MethodInfo? prefix = typeof(LifecyclePatches).GetMethod(patchMethodName, BindingFlags.Static | BindingFlags.NonPublic);
        if (prefix == null)
        {
            log($"Patch skipped: lifecycle stop patch method was not found for {methodName}.");
            return;
        }

        IReadOnlyList<MethodInfo> targets = ReflectionCache.FindMethods("scrController", methodName);
        if (targets.Count == 0)
        {
            targets = ReflectionCache.FindGameMethodsByName(methodName);
        }

        MethodInfo[] distinctTargets = targets
            .Distinct()
            .ToArray();

        if (distinctTargets.Length == 0)
        {
            log($"Patch skipped: {methodName} was not found for lifecycle stop.");
            return;
        }

        foreach (MethodInfo target in distinctTargets)
        {
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        string owners = string.Join(", ", distinctTargets.Select(method => method.DeclaringType?.Name ?? "<unknown>").Distinct().ToArray());
        log($"Patch applied: {methodName} lifecycle stop on {owners} ({distinctTargets.Length} overloads)");
    }

    private static void StartRewindPostfix()
    {
        getService?.Invoke()?.StartFromRewind();
    }

    private static void StopSchedulerPrefix(MethodBase __originalMethod)
    {
        getService?.Invoke()?.Stop($"stop patch: {__originalMethod.Name}");
    }

    private static void Fail2ActionStopSchedulerPrefix(MethodBase __originalMethod)
    {
        if (getSettings?.Invoke().EnableFail2ActionStopPatch != true)
        {
            return;
        }

        getService?.Invoke()?.StopFromFail2Action(__originalMethod.Name);
    }
}
