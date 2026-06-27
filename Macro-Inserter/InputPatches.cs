using System;
using System.Collections.Generic;
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
        PatchHitInputEventCapture(harmony, log);
        PatchPlayerControlUpdate(harmony, log);
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

    private static void PatchPlayerControlUpdate(Harmony harmony, Action<string> log)
    {
        MethodInfo? prefix = typeof(InputPatches).GetMethod(nameof(PlayerControlUpdatePrefix), BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo? postfix = typeof(InputPatches).GetMethod(nameof(PlayerControlUpdatePostfix), BindingFlags.Static | BindingFlags.NonPublic);
        IReadOnlyList<MethodInfo> targets = ReflectionCache.FindMethods("scrController", "PlayerControl_Update");
        if (targets.Count == 0 || prefix == null || postfix == null)
        {
            log("Patch skipped: scrController.PlayerControl_Update was not found.");
            return;
        }

        foreach (MethodInfo target in targets)
        {
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix));
        }

        log($"Patch applied: scrController.PlayerControl_Update ({targets.Count} overloads)");
    }

    private static void PatchHitInputEventCapture(Harmony harmony, Action<string> log)
    {
        Type? inputEventStateType = ReflectionCache.FindType("InputEventState");
        MethodInfo? target = inputEventStateType == null
            ? null
            : ReflectionCache.FindMethod("scrController", "HitInputEvent", typeof(bool), inputEventStateType);
        MethodInfo? prefix = typeof(InputPatches).GetMethod(nameof(HitInputEventPrefix), BindingFlags.Static | BindingFlags.NonPublic);
        if (target == null || prefix == null)
        {
            log("Patch skipped: scrController.HitInputEvent capture was not found.");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        log("Patch applied: scrController.HitInputEvent capture");
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

    private static void HitInputEventPrefix(bool __0, object? __1)
    {
        HitInputEventInvoker.CaptureHumanInputEventState(__0, __1);
    }

    private static void PlayerControlUpdatePrefix()
    {
        getService?.Invoke()?.TickForPlayerControlUpdate();
    }

    private static void PlayerControlUpdatePostfix()
    {
        InputPatchState.ClearFrame();
    }
}
