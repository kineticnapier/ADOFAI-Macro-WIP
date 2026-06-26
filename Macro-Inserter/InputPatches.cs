using System;
using System.Reflection;
using HarmonyLib;

namespace Macro_Inserter;

internal static class InputPatches
{
    private static Func<InternalMacroService?>? getService;

    public static void Apply(Harmony harmony, Action<string> log, Func<InternalMacroService?> serviceAccessor)
    {
        getService = serviceAccessor;

        PatchPostfix(harmony, log, "ValidInputWasTriggered", nameof(ValidInputWasTriggeredPostfix));
        PatchPrefix(harmony, log, "CountValidKeysPressed", nameof(CountValidKeysPressedPrefix));
        PatchUpdateInput(harmony, log);
    }

    private static void PatchPostfix(Harmony harmony, Action<string> log, string gameMethodName, string patchMethodName)
    {
        MethodInfo? target = ReflectionCache.FindMethod("scrController", gameMethodName);
        MethodInfo? patch = typeof(InputPatches).GetMethod(patchMethodName, BindingFlags.Static | BindingFlags.NonPublic);
        if (target == null || patch == null)
        {
            log($"Patch skipped: scrController.{gameMethodName} was not found.");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(patch));
        log($"Patch applied: scrController.{gameMethodName}");
    }

    private static void PatchPrefix(Harmony harmony, Action<string> log, string gameMethodName, string patchMethodName)
    {
        MethodInfo? target = ReflectionCache.FindMethod("scrController", gameMethodName);
        MethodInfo? patch = typeof(InputPatches).GetMethod(patchMethodName, BindingFlags.Static | BindingFlags.NonPublic);
        if (target == null || patch == null)
        {
            log($"Patch skipped: scrController.{gameMethodName} was not found.");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(patch));
        log($"Patch applied: scrController.{gameMethodName}");
    }

    private static void PatchUpdateInput(Harmony harmony, Action<string> log)
    {
        MethodInfo? target = ReflectionCache.FindMethod("scrController", "UpdateInput");
        MethodInfo? prefix = typeof(InputPatches).GetMethod(nameof(UpdateInputPrefix), BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? postfix = typeof(InputPatches).GetMethod(nameof(UpdateInputPostfix), BindingFlags.Static | BindingFlags.NonPublic);
        if (target == null || prefix == null || postfix == null)
        {
            log("Patch skipped: scrController.UpdateInput was not found.");
            return;
        }

        harmony.Patch(
            target,
            prefix: new HarmonyMethod(prefix),
            postfix: new HarmonyMethod(postfix));
        log("Patch applied: scrController.UpdateInput");
    }

    private static void ValidInputWasTriggeredPostfix(ref bool __result)
    {
        if (InputPatchState.HasScheduledInput())
        {
            __result = true;
        }
    }

    private static bool CountValidKeysPressedPrefix(ref int __result)
    {
        if (!InputPatchState.TryGetScheduledKeyCount(out int keyCount))
        {
            return true;
        }

        __result = keyCount;
        return false;
    }

    private static void UpdateInputPrefix()
    {
        getService?.Invoke()?.TickForInputUpdate();
    }

    private static void UpdateInputPostfix()
    {
        InputPatchState.ClearFrame();
    }
}
