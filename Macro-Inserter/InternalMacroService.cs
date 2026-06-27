using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class InternalMacroService
{
    private const float StartFailureLogIntervalSeconds = 1.0f;

    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private readonly MacroPlanBuilder planBuilder;
    private readonly AudioClock audioClock;
    private readonly HitInputEventInvoker hitInputEventInvoker;
    private readonly DirectHitInvoker directHitInvoker;

    private IReadOnlyList<MacroPlanEntry> plan = Array.Empty<MacroPlanEntry>();
    private int nextIndex;
    private bool running;
    private FireMode runningFireMode;
    private float lastStartFailureLogTime = -10.0f;
    private string? lastStartFailureReason;

    public InternalMacroService(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
        planBuilder = new MacroPlanBuilder(log);
        audioClock = new AudioClock(log);
        hitInputEventInvoker = new HitInputEventInvoker(log);
        directHitInvoker = new DirectHitInvoker(settings, log);
    }

    public void Tick()
    {
        if (!EnsureRunningForCurrentSettings())
        {
            return;
        }

        if (settings.FireMode == FireMode.InputPatch)
        {
            return;
        }

        FireDueInputsFromModUpdate();
    }

    public void TickForInputUpdate()
    {
        if (settings.FireMode != FireMode.InputPatch)
        {
            return;
        }

        if (!EnsureRunningForCurrentSettings())
        {
            return;
        }

        FireDueInputsForInputPatch();
    }

    public void StartFromRewind()
    {
        Stop();

        if (!settings.EnableInternalMacro)
        {
            return;
        }

        if (!RuntimeSafety.IsAllowedPlaybackState())
        {
            return;
        }

        if (RuntimeSafety.IsPaused() || RuntimeSafety.IsUiBlockingStart())
        {
            return;
        }

        TryStart();
    }

    private bool EnsureRunningForCurrentSettings()
    {
        if (!settings.EnableInternalMacro)
        {
            Stop();
            return false;
        }

        if (!RuntimeSafety.IsAllowedPlaybackState())
        {
            Stop();
            return false;
        }

        if (RuntimeSafety.IsPaused())
        {
            return false;
        }

        if (!running)
        {
            return false;
        }

        if (runningFireMode != settings.FireMode)
        {
            log("FireMode changed. Stopping internal macro scheduler; it will start again after Start_Rewind.");
            Stop();
            return false;
        }

        return true;
    }

    public void Stop()
    {
        if (!running)
        {
            return;
        }

        running = false;
        plan = Array.Empty<MacroPlanEntry>();
        nextIndex = 0;
        InputPatchState.Reset();
        log("Internal macro scheduler stopped.");
    }

    private bool TryStart()
    {
        MacroPlanBuildResult buildResult = planBuilder.Build(settings.MacroOffsetMs);
        plan = buildResult.Plan;
        if (plan.Count == 0)
        {
            LogStartFailure(buildResult.FailureReason ?? "Internal macro plan is empty.");
            return false;
        }

        if (!audioClock.TryStart(settings.UseAudioTime, out double audioSeconds))
        {
            plan = Array.Empty<MacroPlanEntry>();
            return false;
        }

        nextIndex = ResolveStartIndex(audioSeconds);
        if (nextIndex >= plan.Count)
        {
            log($"Internal macro plan has no remaining entries. entries={plan.Count}, audioTime={audioSeconds:F6}s");
            return false;
        }

        running = true;
        runningFireMode = settings.FireMode;
        MacroPlanEntry firstEntry = plan[nextIndex];
        log($"Internal macro scheduler started. entries={plan.Count}, startIndex={nextIndex}, firstSeqID={firstEntry.SeqId}, firstTargetTime={firstEntry.TargetTimeSeconds:F6}s, audioTime={audioSeconds:F6}s, fireMode={settings.FireMode}, dryRun={settings.DryRun}");
        return true;
    }

    private void LogStartFailure(string reason)
    {
        bool reasonChanged = !string.Equals(reason, lastStartFailureReason, StringComparison.Ordinal);
        if (!reasonChanged && Time.unscaledTime - lastStartFailureLogTime < StartFailureLogIntervalSeconds)
        {
            return;
        }

        lastStartFailureReason = reason;
        lastStartFailureLogTime = Time.unscaledTime;
        log(reason);
    }

    private int ResolveStartIndex(double audioSeconds)
    {
        int byTime = FindFirstAtOrAfterAudioTime(audioSeconds);
        if (!settings.StartFromCurrentFloor)
        {
            return byTime;
        }

        if (!TryReadCurrentFloorSeqId(out int currentFloor))
        {
            return byTime;
        }

        int byFloor = 0;
        while (byFloor < plan.Count && plan[byFloor].SeqId < currentFloor)
        {
            byFloor++;
        }

        return Math.Max(byTime, byFloor);
    }

    private int FindFirstAtOrAfterAudioTime(double audioSeconds)
    {
        const double staleToleranceSeconds = 0.050;
        double lowerBound = audioSeconds - staleToleranceSeconds;

        int index = 0;
        while (index < plan.Count && plan[index].TargetTimeSeconds < lowerBound)
        {
            index++;
        }

        return index;
    }

    private void FireDueInputsFromModUpdate()
    {
        FireDueInputs(allowDirectHit: true, allowInputPatch: false);
    }

    private void FireDueInputsForInputPatch()
    {
        FireDueInputs(allowDirectHit: false, allowInputPatch: true);
    }

    private void FireDueInputs(bool allowDirectHit, bool allowInputPatch)
    {
        if (!audioClock.TryGetSeconds(settings.UseAudioTime, out double audioSeconds))
        {
            Stop();
            return;
        }

        if (nextIndex >= plan.Count)
        {
            Stop();
            return;
        }

        if (!TryReadCurrentFloorSeqId(out int currentFloorSeqId))
        {
            log("Current currFloor.seqID was not available. Stopping internal macro scheduler.");
            Stop();
            return;
        }

        List<MacroPlanEntry> due = new();
        while (nextIndex < plan.Count &&
               plan[nextIndex].TargetTimeSeconds <= audioSeconds &&
               plan[nextIndex].SeqId <= currentFloorSeqId)
        {
            due.Add(plan[nextIndex]);
            nextIndex++;
        }

        if (due.Count == 0)
        {
            return;
        }

        foreach (MacroPlanEntry entry in due)
        {
            double diffMs = (audioSeconds - entry.TargetTimeSeconds) * 1000.0;
            if (settings.DryRun)
            {
                log($"DryRun targetTime={entry.TargetTimeSeconds:F6}s audioTime={audioSeconds:F6}s diffMs={diffMs:F3} seqID={entry.SeqId} currFloorSeqID={currentFloorSeqId}");
            }
        }

        if (settings.DryRun)
        {
            return;
        }

        if (settings.FireMode == FireMode.HitInputEvent)
        {
            if (!allowDirectHit)
            {
                return;
            }

            foreach (MacroPlanEntry entry in due)
            {
                hitInputEventInvoker.Invoke(entry.SeqId, audioSeconds);
            }
        }
        else if (settings.FireMode == FireMode.DirectHit)
        {
            if (!allowDirectHit)
            {
                return;
            }

            foreach (MacroPlanEntry entry in due)
            {
                directHitInvoker.Invoke(entry.SeqId, audioSeconds);
            }
        }
        else
        {
            if (!allowInputPatch)
            {
                return;
            }

            int virtualInputCount = Math.Max(1, settings.VirtualInputKeyCount);
            InputPatchState.BeginFrame(due.Count * virtualInputCount);
            string seqIds = string.Join(",", due.Select(entry => entry.SeqId.ToString()).ToArray());
            log($"InputPatch scheduled count={due.Count} virtualKey={settings.VirtualInputKey} virtualKeyCount={virtualInputCount} audioTime={audioSeconds:F6}s currFloorSeqID={currentFloorSeqId} seqID={seqIds}");
        }
    }

    private static bool TryReadCurrentFloorSeqId(out int seqId)
    {
        seqId = 0;
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            return false;
        }

        object? currFloor = ReflectionCache.ReadMember(controller, "currFloor", "currentFloor");
        if (currFloor == null)
        {
            return ReflectionCache.TryReadInt(controller, out seqId, "floor", "seqID", "currentFloorSeqID");
        }

        if (currFloor is int intValue)
        {
            seqId = intValue;
            return true;
        }

        return ReflectionCache.TryReadInt(currFloor, out seqId, "seqID", "seqId", "floorSeqID");
    }
}
