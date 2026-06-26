using System;
using System.Collections.Generic;
using System.Linq;

namespace Macro_Inserter;

internal sealed class InternalMacroService
{
    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private readonly MacroPlanBuilder planBuilder;
    private readonly AudioClock audioClock;
    private readonly DirectHitInvoker directHitInvoker;

    private IReadOnlyList<MacroPlanEntry> plan = Array.Empty<MacroPlanEntry>();
    private int nextIndex;
    private bool running;
    private FireMode runningFireMode;

    public InternalMacroService(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
        planBuilder = new MacroPlanBuilder(log);
        audioClock = new AudioClock(log);
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
            return TryStart();
        }

        if (runningFireMode != settings.FireMode)
        {
            log("FireMode changed. Restarting internal macro scheduler.");
            Stop();
            return TryStart();
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
        if (RuntimeSafety.IsUiBlockingStart())
        {
            return false;
        }

        if (!audioClock.TryStart(settings.UseAudioTime, out double audioSeconds))
        {
            return false;
        }

        plan = planBuilder.Build(settings.MacroOffsetMs);
        if (plan.Count == 0)
        {
            log("Internal macro plan is empty.");
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

    private int ResolveStartIndex(double audioSeconds)
    {
        int byTime = FindFirstAtOrAfterAudioTime(audioSeconds);
        if (!settings.StartFromCurrentFloor)
        {
            return byTime;
        }

        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null ||
            !ReflectionCache.TryReadInt(controller, out int currentFloor, "currFloor", "currentFloor", "seqID", "floor"))
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

        List<MacroPlanEntry> due = new();
        while (nextIndex < plan.Count && plan[nextIndex].TargetTimeSeconds <= audioSeconds)
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
                log($"DryRun targetTime={entry.TargetTimeSeconds:F6}s audioTime={audioSeconds:F6}s diffMs={diffMs:F3} seqID={entry.SeqId}");
            }
        }

        if (settings.DryRun)
        {
            return;
        }

        if (settings.FireMode == FireMode.DirectHit)
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
            log($"InputPatch scheduled count={due.Count} virtualKey={settings.VirtualInputKey} virtualKeyCount={virtualInputCount} audioTime={audioSeconds:F6}s seqID={seqIds}");
        }
    }
}
