using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Macro_Inserter;

internal sealed class InternalMacroService
{
    private const float StartFailureLogIntervalSeconds = 1.0f;
    private const float ClockLostLogIntervalSeconds = 1.0f;
    private const float CurrentFloorLostLogIntervalSeconds = 1.0f;
    private const float Fail2ActionGraceSeconds = 0.5f;

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
    private ClockMode runningClockMode;
    private float runningStartedAtUnscaledTime;
    private double firstTargetTimeSeconds;
    private float lastStartFailureLogTime = -10.0f;
    private string? lastStartFailureReason;
    private float lastClockLostLogTime = -10.0f;
    private float lastCurrentFloorLostLogTime = -10.0f;

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
    }

    public void TickForPlayerControlUpdate()
    {
        if (!EnsureRunningForCurrentSettings())
        {
            return;
        }

        FireDueInputsFromPlayerControlUpdate();
    }

    public void StartFromRewind()
    {
        if (!settings.EnableInternalMacro)
        {
            Stop("settings disabled");
            return;
        }

        if (!RuntimeSafety.IsAllowedPlaybackState())
        {
            Stop("playback state not allowed");
            return;
        }

        if (RuntimeSafety.IsPaused() || RuntimeSafety.IsUiBlockingStart())
        {
            Stop("playback state not allowed");
            return;
        }

        Stop("start rewind");
        TryStart();
    }

    private bool EnsureRunningForCurrentSettings()
    {
        if (!settings.EnableInternalMacro)
        {
            Stop("settings disabled");
            return false;
        }

        if (!running)
        {
            return false;
        }

        if (runningFireMode != settings.FireMode)
        {
            log("FireMode changed. Stopping internal macro scheduler; it will start again after Start_Rewind.");
            Stop("settings changed: FireMode");
            return false;
        }

        if (runningClockMode != settings.ClockMode)
        {
            log("ClockMode changed. Stopping internal macro scheduler; it will start again after Start_Rewind.");
            Stop("settings changed: ClockMode");
            return false;
        }

        return true;
    }

    public void Stop(string reason)
    {
        if (!running)
        {
            return;
        }

        running = false;
        plan = Array.Empty<MacroPlanEntry>();
        nextIndex = 0;
        InputPatchState.Reset();
        log($"Internal macro scheduler stopped. reason={reason}");
    }

    public void StopFromFail2Action(string methodName)
    {
        if (!running)
        {
            return;
        }

        if (Time.unscaledTime - runningStartedAtUnscaledTime <= Fail2ActionGraceSeconds)
        {
            return;
        }

        if (!audioClock.TryGetSeconds(runningClockMode, out double clockSeconds))
        {
            return;
        }

        if (clockSeconds < firstTargetTimeSeconds)
        {
            return;
        }

        if (!RuntimeSafety.IsControllerFailed())
        {
            return;
        }

        Stop($"stop patch: {methodName}");
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

        if (!audioClock.TryStart(settings.ClockMode, out double clockSeconds))
        {
            plan = Array.Empty<MacroPlanEntry>();
            return false;
        }

        nextIndex = ResolveStartIndex(clockSeconds);
        if (nextIndex >= plan.Count)
        {
            log($"Internal macro plan has no remaining entries. entries={plan.Count}, clockTime={clockSeconds:F6}s, mode={settings.ClockMode}");
            return false;
        }

        running = true;
        runningFireMode = settings.FireMode;
        runningClockMode = settings.ClockMode;
        MacroPlanEntry firstEntry = plan[nextIndex];
        runningStartedAtUnscaledTime = Time.unscaledTime;
        firstTargetTimeSeconds = firstEntry.TargetTimeSeconds;
        log($"Scheduler started. entries={plan.Count}, startIndex={nextIndex}, firstSeqID={firstEntry.SeqId}, firstTargetTime={firstEntry.TargetTimeSeconds:F6}s, clockTime={clockSeconds:F6}s, clockMode={settings.ClockMode}, fireMode={settings.FireMode}, dryRun={settings.DryRun}");
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

        if (byTime == 0 && plan.Count > 0 && audioSeconds < plan[0].TargetTimeSeconds)
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

    private void FireDueInputsFromPlayerControlUpdate()
    {
        FireDueInputs(allowDirectHit: true, allowInputPatch: true);
    }

    private void FireDueInputs(bool allowDirectHit, bool allowInputPatch)
    {
        if (!audioClock.TryGetSeconds(settings.ClockMode, out double clockSeconds))
        {
            LogClockLost();
            return;
        }

        string nextSeqId = nextIndex < plan.Count ? plan[nextIndex].SeqId.ToString() : "<end>";
        string nextTargetTime = nextIndex < plan.Count ? $"{plan[nextIndex].TargetTimeSeconds:F6}s" : "<end>";
        int dueCount = CountDueEntries(clockSeconds);

        if (nextIndex >= plan.Count)
        {
            Stop("end of plan");
            return;
        }

        List<MacroPlanEntry> due = new();
        while (nextIndex < plan.Count &&
               plan[nextIndex].TargetTimeSeconds <= clockSeconds)
        {
            due.Add(plan[nextIndex]);
            nextIndex++;
        }

        if (due.Count == 0)
        {
            return;
        }

        log($"FireDueInputs nextIndex={nextIndex - due.Count} nextSeqID={nextSeqId} nextTargetTime={nextTargetTime} clockTime={clockSeconds:F6}s dueCount={dueCount}");

        if (!TryReadCurrentFloorSeqId(out int currentFloorSeqId))
        {
            LogCurrentFloorLost();
            return;
        }

        foreach (MacroPlanEntry entry in due)
        {
            double diffMs = (clockSeconds - entry.TargetTimeSeconds) * 1000.0;
            log($"Fire attempt: seqID={entry.SeqId} targetTime={entry.TargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} currFloor={currentFloorSeqId} mode={settings.ClockMode} fireMode={settings.FireMode}");
            if (settings.DryRun)
            {
                log($"DryRun targetTime={entry.TargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} seqID={entry.SeqId} currFloorSeqID={currentFloorSeqId} clockMode={settings.ClockMode}");
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
                bool accepted = hitInputEventInvoker.Invoke(entry.SeqId, clockSeconds);
                log($"HitInputEvent result={accepted} seqID={entry.SeqId} clockTime={clockSeconds:F6}s");
                if (!accepted)
                {
                    log("HitInputEvent returned false");
                }
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
                directHitInvoker.Invoke(entry.SeqId, clockSeconds);
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
            log($"InputPatch scheduled count={due.Count} virtualKey={settings.VirtualInputKey} virtualKeyCount={virtualInputCount} clockTime={clockSeconds:F6}s currFloorSeqID={currentFloorSeqId} seqID={seqIds}");
        }
    }

    private int CountDueEntries(double clockSeconds)
    {
        int count = 0;
        int index = nextIndex;
        while (index < plan.Count && plan[index].TargetTimeSeconds <= clockSeconds)
        {
            count++;
            index++;
        }

        return count;
    }

    private void LogClockLost()
    {
        if (Time.unscaledTime - lastClockLostLogTime < ClockLostLogIntervalSeconds)
        {
            return;
        }

        lastClockLostLogTime = Time.unscaledTime;
        log($"clock lost: mode={settings.ClockMode}");
    }

    private void LogCurrentFloorLost()
    {
        if (Time.unscaledTime - lastCurrentFloorLostLogTime < CurrentFloorLostLogIntervalSeconds)
        {
            return;
        }

        lastCurrentFloorLostLogTime = Time.unscaledTime;
        log("Current currFloor.seqID was not available. Keeping internal macro scheduler running.");
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
