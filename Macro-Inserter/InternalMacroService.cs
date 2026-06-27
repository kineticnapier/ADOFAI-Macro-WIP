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
    private const float ClockResetWaitLogIntervalSeconds = 1.0f;
    private const float PlayerControlTickLogIntervalSeconds = 1.0f;
    private const float FireSkipLogIntervalSeconds = 1.0f;
    private const double StaleClockStartMarginSeconds = 5.0;

    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private readonly MacroPlanBuilder planBuilder;
    private readonly AudioClock audioClock;
    private readonly HitInputEventInvoker hitInputEventInvoker;
    private readonly DirectHitInvoker directHitInvoker;

    private IReadOnlyList<MacroPlanEntry> plan = Array.Empty<MacroPlanEntry>();
    private int nextIndex;
    private bool armed;
    private bool running;
    private FireMode runningFireMode;
    private ClockMode runningClockMode;
    private float lastStartFailureLogTime = -10.0f;
    private string? lastStartFailureReason;
    private float lastClockLostLogTime = -10.0f;
    private float lastCurrentFloorLostLogTime = -10.0f;
    private float lastClockResetWaitLogTime = -10.0f;
    private float lastPlayerControlTickLogTime = -10.0f;
    private float lastFireSkipLogTime = -10.0f;

    public InternalMacroService(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
        planBuilder = new MacroPlanBuilder(log);
        audioClock = new AudioClock(log);
        hitInputEventInvoker = new HitInputEventInvoker(settings, log);
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
        LogPlayerControlUpdateTick();

        if (!settings.EnableInternalMacro)
        {
            Stop("settings disabled");
            return;
        }

        if (armed && !running)
        {
            if (TryStartArmedFromPlayerControlUpdate())
            {
                return;
            }
        }

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
        BuildPlanAndArm();
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
        bool wasRunning = running;
        armed = false;
        running = false;
        plan = Array.Empty<MacroPlanEntry>();
        nextIndex = 0;
        InputPatchState.Reset();

        if (!wasRunning)
        {
            return;
        }

        log($"Internal macro scheduler stopped. reason={reason}");
    }

    private bool BuildPlanAndArm()
    {
        MacroPlanBuildResult buildResult = planBuilder.Build(settings.MacroOffsetMs);
        plan = buildResult.Plan;
        if (plan.Count == 0)
        {
            LogStartFailure(buildResult.FailureReason ?? "Internal macro plan is empty.");
            return false;
        }

        armed = true;
        nextIndex = 0;
        MacroPlanEntry firstEntry = plan[0];
        log($"Scheduler armed. entries={plan.Count}, firstSeqID={firstEntry.SeqId}, firstTargetTime={firstEntry.TargetTimeSeconds:F6}s, clockMode={settings.ClockMode}, fireMode={settings.FireMode}, dryRun={settings.DryRun}");
        return true;
    }

    private bool TryStartArmedFromPlayerControlUpdate()
    {
        if (plan.Count == 0)
        {
            armed = false;
            return false;
        }

        if (!RuntimeSafety.IsAllowedPlaybackState() || RuntimeSafety.IsPaused() || RuntimeSafety.IsUiBlockingStart())
        {
            return false;
        }

        if (!audioClock.TryStart(settings.ClockMode, out double clockSeconds, logReady: false))
        {
            return false;
        }

        MacroPlanEntry firstPlanEntry = plan[0];
        bool hasCurrentFloor = TryReadCurrentFloorSeqId(out int currentFloor);
        if (hasCurrentFloor && IsStaleClockAtBeginning(clockSeconds, currentFloor, firstPlanEntry.TargetTimeSeconds))
        {
            LogWaitingForClockReset(clockSeconds, currentFloor, firstPlanEntry.TargetTimeSeconds);
            return false;
        }

        nextIndex = ResolveStartIndex(clockSeconds, hasCurrentFloor, currentFloor);
        if (nextIndex >= plan.Count)
        {
            log($"Internal macro plan has no remaining entries. entries={plan.Count}, clockTime={clockSeconds:F6}s, mode={settings.ClockMode}");
            armed = false;
            return false;
        }

        armed = false;
        running = true;
        runningFireMode = settings.FireMode;
        runningClockMode = settings.ClockMode;
        MacroPlanEntry firstEntry = plan[nextIndex];
        string currentFloorText = hasCurrentFloor ? currentFloor.ToString() : "<unavailable>";
        log($"Scheduler started. entries={plan.Count}, startIndex={nextIndex}, firstSeqID={firstEntry.SeqId}, firstTargetTime={firstEntry.TargetTimeSeconds:F6}s, clockTime={clockSeconds:F6}s, currentFloor={currentFloorText}, clockMode={settings.ClockMode}, fireMode={settings.FireMode}, dryRun={settings.DryRun}");
        FireDueInputs(allowDirectHit: true, allowInputPatch: true);
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

    private int ResolveStartIndex(double audioSeconds, bool hasCurrentFloor, int currentFloor)
    {
        if (!hasCurrentFloor)
        {
            return FindFirstAtOrAfterAudioTime(audioSeconds);
        }

        int byFloor = 0;
        while (byFloor < plan.Count && plan[byFloor].SeqId < currentFloor)
        {
            byFloor++;
        }

        return byFloor;
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

        if (TryReadCurrentFloorSeqId(out int currentFloorSeqId) &&
            plan.Count > 0 &&
            IsStaleClockAtBeginning(clockSeconds, currentFloorSeqId, plan[0].TargetTimeSeconds))
        {
            LogFireSkip($"clock stale: currentFloor={currentFloorSeqId} firstTargetTime={plan[0].TargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s");
            return;
        }

        if (dueCount == 0)
        {
            LogFireSkip($"dueCount=0 nextIndex={nextIndex} nextSeqID={nextSeqId} nextTargetTime={nextTargetTime} clockTime={clockSeconds:F6}s");
            return;
        }

        if (!TryReadCurrentFloorSeqId(out currentFloorSeqId))
        {
            LogCurrentFloorLost();
            return;
        }

        log($"FireDueInputs nextIndex={nextIndex} nextSeqID={nextSeqId} nextTargetTime={nextTargetTime} clockTime={clockSeconds:F6}s dueCount={dueCount}");

        List<MacroPlanEntry> due = new();
        while (nextIndex < plan.Count &&
               plan[nextIndex].TargetTimeSeconds <= clockSeconds)
        {
            MacroPlanEntry entry = plan[nextIndex];
            if (currentFloorSeqId > entry.SeqId)
            {
                log($"floorGuard skipped: currentFloor={currentFloorSeqId} targetSeqID={entry.SeqId}");
                nextIndex++;
                continue;
            }

            due.Add(entry);
            nextIndex++;
        }

        if (due.Count == 0)
        {
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

    private static bool IsStaleClockAtBeginning(double clockSeconds, int currentFloor, double firstTargetTime)
    {
        return currentFloor <= 1 &&
               clockSeconds > firstTargetTime + StaleClockStartMarginSeconds;
    }

    private void LogWaitingForClockReset(double clockSeconds, int currentFloor, double firstTargetTime)
    {
        if (Time.unscaledTime - lastClockResetWaitLogTime < ClockResetWaitLogIntervalSeconds)
        {
            return;
        }

        lastClockResetWaitLogTime = Time.unscaledTime;
        log($"waiting for clock reset: currentFloor={currentFloor} firstTargetTime={firstTargetTime:F6}s clockTime={clockSeconds:F6}s");
    }

    private void LogPlayerControlUpdateTick()
    {
        if (Time.unscaledTime - lastPlayerControlTickLogTime < PlayerControlTickLogIntervalSeconds)
        {
            return;
        }

        lastPlayerControlTickLogTime = Time.unscaledTime;
        log($"PlayerControl_Update tick received. armed={armed} running={running}");
    }

    private void LogFireSkip(string reason)
    {
        if (Time.unscaledTime - lastFireSkipLogTime < FireSkipLogIntervalSeconds)
        {
            return;
        }

        lastFireSkipLogTime = Time.unscaledTime;
        log($"FireDueInputs skipped: {reason}");
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
