using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Macro_Inserter;

internal static class InputPatches
{
    private static Func<InternalMacroService?>? getService;
    private static Action<string>? log;
    private static MethodInfo? simulatedPlayerControlUpdate;
    private static bool forcingSimulatedUpdate;

    public static void Apply(Harmony harmony, Action<string> log, Func<InternalMacroService?> serviceAccessor)
    {
        InputPatches.log = log;
        getService = serviceAccessor;

        PatchPrefix(harmony, log, "ValidInputWasTriggered", nameof(ValidInputWasTriggeredPrefix));
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
        simulatedPlayerControlUpdate = ReflectionCache.FindMethod("scrController", "Simulated_PlayerControl_Update", typeof(ulong?));
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
        if (simulatedPlayerControlUpdate == null)
        {
            log("Patch warning: scrController.Simulated_PlayerControl_Update was not found; async-input fallback cannot force game input simulation.");
        }
        else
        {
            log("Patch prepared: scrController.Simulated_PlayerControl_Update async-input fallback");
        }
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

    private static bool ValidInputWasTriggeredPrefix(ref bool __result)
    {
        if (!InputPatchState.HasScheduledInput())
        {
            return true;
        }

        // Do not let the original ValidInputWasTriggered() run here.
        // The original method calls CountValidKeysPressed() internally and also
        // requires a real RDInput key edge. If we let it run for synthetic input,
        // CountValidKeysPressed consumes the queued virtual key during the check,
        // then HitAutoFloors() returns before it can add keyTimes.
        //
        // Returning true here preserves the game's normal outer flow:
        //   HitAutoFloors() sees a triggered input
        //   HitAutoFloors() then calls CountValidKeysPressed() once
        //   that call consumes the virtual key and adds keyTimes
        __result = true;
        return false;
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

    private static bool HitInputEventPrefix(bool __0, object? __1, ref bool __result)
    {
        if (InputPatchState.TryConsumeSyntheticHitInputEvent(__1, out int remainingBudget))
        {
            __result = true;
            return false;
        }

        HitInputEventInvoker.CaptureHumanInputEventState(__0, __1);
        return true;
    }

    private static void PlayerControlUpdatePrefix(object __instance)
    {
        if (forcingSimulatedUpdate)
        {
            return;
        }

        getService?.Invoke()?.TickForPlayerControlUpdate();

        if (!InputPatchState.HasScheduledInput())
        {
            return;
        }

        if (!ReflectionCache.TryReadBool("AsyncInputManager", out bool asyncInputActive, "isActive") || !asyncInputActive)
        {
            return;
        }

        if (simulatedPlayerControlUpdate == null)
        {
            return;
        }

        try
        {
            forcingSimulatedUpdate = true;
            simulatedPlayerControlUpdate.Invoke(__instance, new object?[] { null });
        }
        catch (Exception ex)
        {
            log?.Invoke($"Forced Simulated_PlayerControl_Update failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            forcingSimulatedUpdate = false;
        }
    }

    private static void PlayerControlUpdatePostfix()
    {
        InputPatchState.ClearFrame();
    }
}
