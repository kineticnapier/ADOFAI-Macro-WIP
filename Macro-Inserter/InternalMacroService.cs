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
        directHitInvoker = new DirectHitInvoker(log);
    }

    public void Tick()
    {
        if (!settings.EnableInternalMacro)
        {
            Stop();
            return;
        }

        if (!RuntimeSafety.IsAllowedPlaybackState())
        {
            Stop();
            return;
        }

        if (RuntimeSafety.IsPaused())
        {
            return;
        }

        if (!running)
        {
            TryStart();
            return;
        }

        if (runningFireMode != settings.FireMode)
        {
            log("FireMode changed. Restarting internal macro scheduler.");
            Stop();
            TryStart();
            return;
        }

        FireDueInputs();
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

    private void TryStart()
    {
        if (RuntimeSafety.IsUiBlockingStart())
        {
            return;
        }

        if (!audioClock.TryStart(settings.UseAudioTime, out double audioSeconds))
        {
            return;
        }

        plan = planBuilder.Build(settings.MacroOffsetMs);
        if (plan.Count == 0)
        {
            log("Internal macro plan is empty.");
            return;
        }

        nextIndex = ResolveStartIndex(audioSeconds);
        running = true;
        runningFireMode = settings.FireMode;
        log($"Internal macro scheduler started. entries={plan.Count}, startIndex={nextIndex}, audioTime={audioSeconds:F6}s, fireMode={settings.FireMode}, dryRun={settings.DryRun}");

        FireDueInputs();
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

    private void FireDueInputs()
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
            foreach (MacroPlanEntry _ in due)
            {
                directHitInvoker.Invoke();
            }
        }
        else
        {
            InputPatchState.BeginFrame(due.Count);
            string seqIds = string.Join(",", due.Select(entry => entry.SeqId.ToString()).ToArray());
            log($"InputPatch scheduled count={due.Count} audioTime={audioSeconds:F6}s seqID={seqIds}");
        }
    }
}
