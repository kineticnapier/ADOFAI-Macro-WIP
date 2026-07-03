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
    private const int AdaptiveDiffWindowSize = 30;
    private const double AdaptiveLerpFactor = 0.05;
    private const double AdaptiveMaxAbsMs = 30.0;
    private const float DuplicateStartRewindWindowSeconds = 0.5f;
    private const double RestartClockResetSeconds = 0.2;
    private const double RestartClockBackstepSeconds = 0.5;
    private const int DueBacklogFailureMultiplier = 4;
    private const int MinDueBacklogFailureThreshold = 16;

    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private readonly MacroPlanBuilder planBuilder;
    private readonly AudioClock audioClock;
    private readonly HitInputEventInvoker hitInputEventInvoker;
    private readonly DirectHitInvoker directHitInvoker;

    private IReadOnlyList<MacroPlanEntry> plan = Array.Empty<MacroPlanEntry>();
    private IReadOnlyList<MacroPlanEntry> cachedPlan = Array.Empty<MacroPlanEntry>();
    private IReadOnlyList<InputPlanEntry> inputPlan = Array.Empty<InputPlanEntry>();
    private double cachedPlanOffsetMs = double.NaN;
    private string? cachedPlanSourceKey;
    private int cachedDetectedMidspinCount;
    private int cachedSkippedDuplicateTimeCount;
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
    private float lastStartRewindUnityTime = -10.0f;
    private double lastStartRewindClockTime = -1.0;
    private bool firstInputPatchScheduled;
    private int hitDiffSampleCount;
    private double hitDiffTotalMs;
    private double hitDiffMaxAbsMs;
    private readonly Queue<double> recentDirectHitDiffMs = new();
    private double adaptiveOffsetMs;
    private double medianDispatchLagMs;
    private bool suppressNextAdaptiveCorrection;
    private long macroKeyViewerHitCounter;

    public MacroKeyViewerState MacroKeyViewer { get; } = new();
    public int HitDiffSampleCount => hitDiffSampleCount;
    public double AverageHitDiffMs => hitDiffSampleCount == 0 ? 0.0 : hitDiffTotalMs / hitDiffSampleCount;
    public double MaxAbsHitDiffMs => hitDiffMaxAbsMs;
    public double AdaptiveOffsetMs => adaptiveOffsetMs;
    public double EffectiveOffsetMs => settings.MacroOffsetMs + (settings.EnableAdaptiveOffset ? adaptiveOffsetMs : 0.0);
    public double MedianDispatchLagMs => medianDispatchLagMs;
    public int DetectedMidspinCount => cachedDetectedMidspinCount;
    public int SkippedDuplicateTimeCount => cachedSkippedDuplicateTimeCount;

    public InternalMacroService(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
        planBuilder = new MacroPlanBuilder(log);
        audioClock = new AudioClock(log);
        hitInputEventInvoker = new HitInputEventInvoker(settings, log);
        directHitInvoker = new DirectHitInvoker(settings, log);
    }

    public void Warmup()
    {
        RuntimeWarmup.TrySetDotweenCapacity();
        RebuildCachedPlan();
        audioClock.TryStart(settings.ClockMode, out _, logReady: false);
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller != null)
        {
            ReflectionCache.ReadMember(controller, "currFloor", "currentFloor");
            object? chosenPlanet = ReflectionCache.ReadMember(controller, "chosenPlanet", "selectedPlanet");
            ReflectionCache.ReadMember(controller, "nextfloor", "nextFloor");
            if (chosenPlanet != null)
            {
                object? planetCurrFloor = ReflectionCache.ReadMember(chosenPlanet, "currfloor", "currFloor", "currentFloor");
                if (planetCurrFloor != null)
                {
                    ReflectionCache.ReadMember(planetCurrFloor, "nextfloor", "nextFloor", "next");
                }
            }
        }

        ReflectionCache.WarmupMembers(
            "scrController",
            "instance",
            "Instance",
            "inst",
            "currFloor",
            "currentFloor",
            "chosenPlanet",
            "selectedPlanet",
            "nextfloor",
            "nextFloor",
            "floor",
            "seqID",
            "currentFloorSeqID");
        ReflectionCache.WarmupMembers("scrLevelMaker", "instance", "Instance", "inst", "listFloors", "floors");
        ReflectionCache.WarmupMembers("scrFloor", "seqID", "seqId", "floorSeqID", "nextfloor", "nextFloor", "next");
        ReflectionCache.WarmupMembers("scrPlanet", "currfloor", "currFloor", "currentFloor", "targetExitAngle", "midspinInfiniteMargin", "responsive", "cachedAngle");
        directHitInvoker.Warmup();
        log($"Warmup Macro completed. planEntries={cachedPlan.Count}, detectedMidspin={cachedDetectedMidspinCount}, skippedDuplicateTime={cachedSkippedDuplicateTimeCount}, fireMode={settings.FireMode}, clockMode={settings.ClockMode}");
    }

    public void ResetMacroKeyViewer()
    {
        macroKeyViewerHitCounter = 0;
        MacroKeyViewer.ResetCounters();
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

        bool hasClock = audioClock.TryReadCurrentSeconds(settings.ClockMode, out double clockSeconds);
        bool hasCurrentFloor = TryReadCurrentFloorSeqId(out int currentFloor);
        if ((running || armed) && hasCurrentFloor && currentFloor <= 0)
        {
            LogStartRewindReceived(
                running,
                armed,
                hasCurrentFloor,
                currentFloor,
                hasClock,
                clockSeconds,
                action: "rearm floor-reset-while-active");

            RecordStartRewindState(hasClock, clockSeconds);
            Stop("start rewind floor reset while scheduler active");
            BuildPlanAndArm();
            return;
        }

        if (running || armed)
        {
            LogStartRewindReceived(
                running,
                armed,
                hasCurrentFloor,
                currentFloor,
                hasClock,
                clockSeconds,
                action: "ignored scheduler-active");

            RecordStartRewindState(hasClock, clockSeconds);
            return;
        }

        if (running || armed)
        {
            LogStartRewindReceived(
                running,
                armed,
                hasCurrentFloor,
                currentFloor,
                hasClock,
                clockSeconds,
                action: "ignored scheduler-active");
            RecordStartRewindState(hasClock, clockSeconds);
            return;
        }

        LogStartRewindReceived(
            running,
            armed,
            hasCurrentFloor,
            currentFloor,
            hasClock,
            clockSeconds,
            action: "rearm");
        RecordStartRewindState(hasClock, clockSeconds);
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

    private bool IsDuplicateStartRewind(
        bool hasClock,
        double clockSeconds,
        bool hasCurrentFloor,
        int currentFloor,
        out string reason)
    {
        reason = "duplicate";
        bool clockReset = hasClock && clockSeconds < RestartClockResetSeconds;
        bool floorReset = hasCurrentFloor && currentFloor <= 0;
        bool clockBackstepped = hasClock &&
                                lastStartRewindClockTime >= 0.0 &&
                                clockSeconds < lastStartRewindClockTime - RestartClockBackstepSeconds;
        if (clockReset || floorReset || clockBackstepped)
        {
            reason = "restart";
            return false;
        }

        if (Time.unscaledTime - lastStartRewindUnityTime < DuplicateStartRewindWindowSeconds)
        {
            reason = "duplicate-window";
            return true;
        }

        if (hasCurrentFloor &&
            currentFloor >= 1 &&
            hasClock &&
            clockSeconds > DuplicateStartRewindWindowSeconds)
        {
            reason = "playback-active";
            return true;
        }

        if (hasClock &&
            lastStartRewindClockTime >= 0.0 &&
            clockSeconds >= lastStartRewindClockTime - 0.050)
        {
            reason = "clock-not-reset";
            return true;
        }

        return false;
    }

    private void RecordStartRewindState(bool hasClock, double clockSeconds)
    {
        lastStartRewindUnityTime = Time.unscaledTime;
        if (hasClock)
        {
            lastStartRewindClockTime = clockSeconds;
        }
    }

    private void LogStartRewindReceived(
        bool wasRunning,
        bool wasArmed,
        bool hasCurrentFloor,
        int currentFloor,
        bool hasClock,
        double clockSeconds,
        string action)
    {
        string currentFloorText = hasCurrentFloor ? currentFloor.ToString() : "<unavailable>";
        string clockText = hasClock ? $"{clockSeconds:F6}s" : "<unavailable>";
        string lastClockText = lastStartRewindClockTime >= 0.0
            ? $"{lastStartRewindClockTime:F6}s"
            : "<none>";
        log($"Start_Rewind received. running={wasRunning} armed={wasArmed} currentFloor={currentFloorText} clockTime={clockText} lastStartRewindClockTime={lastClockText} action={action}");
    }

    public void Stop(string reason)
    {
        bool wasRunning = running;
        bool wasArmed = armed;

        armed = false;
        running = false;
        plan = Array.Empty<MacroPlanEntry>();
        inputPlan = Array.Empty<InputPlanEntry>();
        nextIndex = 0;
        firstInputPatchScheduled = false;
        InputPatchState.Reset();

        if (!wasRunning && !wasArmed)
        {
            return;
        }

        log($"Internal macro scheduler stopped. reason={reason}, wasRunning={wasRunning}, wasArmed={wasArmed}");
    }

    private bool BuildPlanAndArm()
    {
        plan = GetOrBuildCachedPlan();
        if (plan.Count == 0)
        {
            LogStartFailure("Internal macro plan is empty.");
            return false;
        }

        inputPlan = BuildInputPlan(plan);
        if (inputPlan.Count == 0)
        {
            LogStartFailure("Internal input plan is empty.");
            return false;
        }

        armed = true;
        firstInputPatchScheduled = false;
        ResetHitDiffStats();
        ResetAdaptiveOffset();
        directHitInvoker.ResetRunState();
        nextIndex = 0;
        ResetMacroKeyViewer();
        InputPlanEntry firstEntry = inputPlan[0];
        log($"Scheduler armed. macroEntries={plan.Count}, inputEntries={inputPlan.Count}, firstSeqID={firstEntry.FirstSeqId}, firstTargetTime={firstEntry.FirstTargetTimeSeconds:F6}s, clockMode={settings.ClockMode}, fireMode={settings.FireMode}, dryRun={settings.DryRun}");
        return true;
    }

    private IReadOnlyList<MacroPlanEntry> GetOrBuildCachedPlan()
    {
        if (cachedPlan.Count > 0 &&
            Math.Abs(cachedPlanOffsetMs - settings.MacroOffsetMs) < 0.000001 &&
            string.Equals(cachedPlanSourceKey, ReadPlanSourceKey(), StringComparison.Ordinal))
        {
            return cachedPlan;
        }

        return RebuildCachedPlan();
    }

    private IReadOnlyList<MacroPlanEntry> RebuildCachedPlan()
    {
        MacroPlanBuildResult buildResult = planBuilder.Build(
            settings.MacroOffsetMs,
            logPreview: settings.LoggingMode == LoggingMode.Verbose);
        cachedPlan = buildResult.Plan;
        cachedPlanOffsetMs = settings.MacroOffsetMs;
        cachedPlanSourceKey = ReadPlanSourceKey();
        cachedDetectedMidspinCount = buildResult.DetectedMidspinCount;
        cachedSkippedDuplicateTimeCount = buildResult.SkippedDuplicateTimeCount;
        if (cachedPlan.Count == 0)
        {
            LogStartFailure(buildResult.FailureReason ?? "Internal macro plan is empty.");
        }

        return cachedPlan;
    }

    private IReadOnlyList<InputPlanEntry> BuildInputPlan(IReadOnlyList<MacroPlanEntry> macroPlan)
    {
        if (macroPlan.Count == 0)
        {
            return Array.Empty<InputPlanEntry>();
        }

        double windowSeconds = Math.Max(0.0, settings.PseudoChordWindowMs) / 1000.0;
        int maxHitsPerGroup = Math.Max(1, settings.MaxHitsPerPseudoChordGroup);
        int keyCapacity = GetPseudoChordKeyCapacity(maxHitsPerGroup);
        List<InputPlanEntry> entries = new();

        int index = 0;
        while (index < macroPlan.Count)
        {
            int startIndex = index;
            MacroPlanEntry first = macroPlan[startIndex];
            MacroPlanEntry last = first;
            bool containsMidspin = first.IsMidspin;
            bool isNearMidspin = first.IsNearMidspin;
            index++;

            while (index < macroPlan.Count)
            {
                MacroPlanEntry candidate = macroPlan[index];
                double deltaSeconds = Math.Abs(candidate.TargetTimeSeconds - last.TargetTimeSeconds);
                if (deltaSeconds > windowSeconds)
                {
                    break;
                }

                last = candidate;
                containsMidspin |= candidate.IsMidspin;
                isNearMidspin |= candidate.IsNearMidspin;
                index++;
            }

            int rawEntryCount = index - startIndex;
            int emittedHitCount = Math.Min(rawEntryCount, Math.Min(keyCapacity, maxHitsPerGroup));
            entries.Add(new InputPlanEntry(
                startIndex,
                index,
                first.SeqId,
                last.SeqId,
                first.TargetTimeSeconds,
                last.TargetTimeSeconds,
                rawEntryCount,
                Math.Max(1, emittedHitCount),
                containsMidspin,
                isNearMidspin));
        }

        return entries;
    }

    private int GetPseudoChordKeyCapacity(int fallback)
    {
        int viewerKeyCount = MacroKeyViewerState.ParseKeyNames(settings.MacroKeyViewerKeysText).Length;
        return Math.Max(1, Math.Max(Math.Max(1, settings.VirtualInputKeyCount), viewerKeyCount > 0 ? viewerKeyCount : fallback));
    }

    private static string ReadPlanSourceKey()
    {
        object? levelMaker = ReflectionCache.GetSingletonInstance("scrLevelMaker");
        object? floors = levelMaker == null
            ? null
            : ReflectionCache.ReadMember(levelMaker, "listFloors", "floors");
        if (floors == null)
        {
            return "no-levelmaker-floors";
        }

        object[] floorArray = ReflectionCache.AsEnumerable(floors).Cast<object>().ToArray();
        int count = ReflectionCache.TryReadInt(floors, out int readCount, "Count", "count")
            ? readCount
            : floorArray.Length;
        string first = floorArray.Length > 0 ? ReadFloorKeyPart(floorArray[0]) : "<none>";
        string last = floorArray.Length > 0 ? ReadFloorKeyPart(floorArray[floorArray.Length - 1]) : "<none>";
        string levelPath = ReadLevelPath(levelMaker);
        return $"{floors.GetHashCode()}:{count}:{first}:{last}:{levelPath}";
    }

    private static string ReadFloorKeyPart(object floor)
    {
        int seqId = ReflectionCache.TryReadInt(floor, out int readSeqId, "seqID", "seqId", "floorSeqID")
            ? readSeqId
            : -1;
        object? rawTime = ReflectionCache.ReadMember(floor, "entryTimePitchAdj", "entryTime", "entryTimeSeconds");
        string time = rawTime == null ? "<no-time>" : rawTime.ToString() ?? "<null-time>";
        return $"{seqId}@{time}";
    }

    private static string ReadLevelPath(object? levelMaker)
    {
        if (levelMaker == null)
        {
            return "<no-levelmaker>";
        }

        object? rawPath = ReflectionCache.ReadMember(
            levelMaker,
            "levelPath",
            "filePath",
            "path",
            "levelFile",
            "levelFilePath",
            "loadedLevelPath",
            "currentLevelPath");
        return rawPath?.ToString() ?? "<no-path>";
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

        InputPlanEntry firstPlanEntry = inputPlan[0];
        bool hasCurrentFloor = TryReadCurrentFloorSeqId(out int currentFloor);
        if (hasCurrentFloor && IsStaleClockAtBeginning(clockSeconds, currentFloor, firstPlanEntry.FirstTargetTimeSeconds))
        {
            LogWaitingForClockReset(clockSeconds, currentFloor, firstPlanEntry.FirstTargetTimeSeconds);
            return false;
        }

        if (ShouldWaitForManualFirstHit(hasCurrentFloor, currentFloor))
        {
            LogFireSkip($"manual first hit waiting: currentFloor={currentFloor} targetSeqID={firstPlanEntry.FirstSeqId} clockTime={clockSeconds:F6}s");
            return false;
        }

        if (ShouldUseInputPatchFirstHit(hasCurrentFloor, currentFloor))
        {
            ScheduleFirstInputPatch(clockSeconds, currentFloor, firstPlanEntry);
            return false;
        }

        if (ShouldWaitForInputPatchFirstHit(hasCurrentFloor, currentFloor))
        {
            LogFireSkip($"InputPatch first hit waiting: currentFloor={currentFloor} targetSeqID={firstPlanEntry.FirstSeqId} clockTime={clockSeconds:F6}s");
            return false;
        }

        nextIndex = ResolveStartIndex(clockSeconds, hasCurrentFloor, currentFloor);
        if (nextIndex >= inputPlan.Count)
        {
            log($"Internal input plan has no remaining entries. macroEntries={plan.Count}, inputEntries={inputPlan.Count}, clockTime={clockSeconds:F6}s, mode={settings.ClockMode}");
            armed = false;
            return false;
        }

        armed = false;
        running = true;
        runningFireMode = settings.FireMode;
        runningClockMode = settings.ClockMode;
        InputPlanEntry firstEntry = inputPlan[nextIndex];
        string currentFloorText = hasCurrentFloor ? currentFloor.ToString() : "<unavailable>";
        log($"Scheduler started. macroEntries={plan.Count}, inputEntries={inputPlan.Count}, startIndex={nextIndex}, firstSeqID={firstEntry.FirstSeqId}, firstTargetTime={firstEntry.FirstTargetTimeSeconds:F6}s, clockTime={clockSeconds:F6}s, currentFloor={currentFloorText}, clockMode={settings.ClockMode}, fireMode={settings.FireMode}, dryRun={settings.DryRun}");
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
        if (!settings.StartFromCurrentFloor || !hasCurrentFloor)
        {
            return FindFirstAtOrAfterAudioTime(audioSeconds);
        }

        int byFloor = 0;
        while (byFloor < inputPlan.Count && inputPlan[byFloor].LastSeqId <= currentFloor)
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
        while (index < inputPlan.Count && inputPlan[index].FirstTargetTimeSeconds < lowerBound)
        {
            index++;
        }

        return index;
    }

    private bool ShouldWaitForManualFirstHit(bool hasCurrentFloor, int currentFloor)
    {
        return settings.FirstHitMode == FirstHitMode.Manual &&
               hasCurrentFloor &&
               currentFloor < 1 &&
               inputPlan.Count > 0 &&
               inputPlan[0].FirstSeqId == 1;
    }

    private bool ShouldUseInputPatchFirstHit(bool hasCurrentFloor, int currentFloor)
    {
        return settings.FirstHitMode == FirstHitMode.InputPatch &&
               hasCurrentFloor &&
               currentFloor < 1 &&
               inputPlan.Count > 0 &&
               inputPlan[0].FirstSeqId == 1 &&
               !firstInputPatchScheduled;
    }

    private bool ShouldWaitForInputPatchFirstHit(bool hasCurrentFloor, int currentFloor)
    {
        return settings.FirstHitMode == FirstHitMode.InputPatch &&
               hasCurrentFloor &&
               currentFloor < 1 &&
               inputPlan.Count > 0 &&
               inputPlan[0].FirstSeqId == 1 &&
               firstInputPatchScheduled;
    }

    private void ScheduleFirstInputPatch(double clockSeconds, int currentFloor, InputPlanEntry firstEntry)
    {
        int virtualInputCount = Math.Max(1, settings.VirtualInputKeyCount);
        InputPatchState.BeginFrame(virtualInputCount);
        firstInputPatchScheduled = true;
        log($"FirstHitMode InputPatch scheduled. currentFloor={currentFloor} targetSeqID={firstEntry.FirstSeqId} targetTime={firstEntry.FirstTargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s virtualKey={settings.VirtualInputKey} virtualKeyCount={virtualInputCount}");
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

        string nextSeqId = nextIndex < inputPlan.Count ? inputPlan[nextIndex].FirstSeqId.ToString() : "<end>";
        string nextTargetTime = nextIndex < inputPlan.Count ? $"{GetEffectiveTargetTimeSeconds(inputPlan[nextIndex]):F6}s" : "<end>";
        int dueCount = CountDueEntries(clockSeconds);

        if (nextIndex >= inputPlan.Count)
        {
            Stop("end of plan");
            return;
        }

        if (TryReadCurrentFloorSeqId(out int currentFloorSeqId) &&
            inputPlan.Count > 0 &&
            IsStaleClockAtBeginning(clockSeconds, currentFloorSeqId, inputPlan[0].FirstTargetTimeSeconds))
        {
            LogFireSkip($"clock stale: currentFloor={currentFloorSeqId} firstTargetTime={inputPlan[0].FirstTargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s");
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

        LogVerbose($"FireDueInputs nextIndex={nextIndex} nextSeqID={nextSeqId} nextTargetTime={nextTargetTime} clockTime={clockSeconds:F6}s dueCount={dueCount}");

        int maxHitsThisUpdate = GetMaxHitsPerPlayerControlUpdate();
        int hitsThisUpdate = 0;
        if (settings.FireMode == FireMode.DirectHit &&
            HandleDueCountTooLarge(dueCount, maxHitsThisUpdate, clockSeconds))
        {
            if (!running)
            {
                return;
            }

            dueCount = CountDueEntries(clockSeconds);
        }

        while (nextIndex < inputPlan.Count)
        {
            InputPlanEntry inputEntry = inputPlan[nextIndex];
            MacroPlanEntry entry = plan[inputEntry.PlanStartIndex];
            double effectiveTargetTimeSeconds = GetEffectiveTargetTimeSeconds(entry);
            if (effectiveTargetTimeSeconds > clockSeconds)
            {
                break;
            }

            if (!TryReadCurrentFloorSeqId(out currentFloorSeqId))
            {
                LogCurrentFloorLost();
                break;
            }

            double diffMs = (clockSeconds - effectiveTargetTimeSeconds) * 1000.0;
            if (currentFloorSeqId >= inputEntry.FirstSeqId)
            {
                string floorGuardReason = currentFloorSeqId >= inputEntry.LastSeqId
                    ? "already passed group"
                    : "already inside group";
                LogVerbose($"floorGuard skipped: {floorGuardReason}. currentFloor={currentFloorSeqId} targetSeqID={inputEntry.FirstSeqId}-{inputEntry.LastSeqId}");
                nextIndex++;
                continue;
            }

            if (currentFloorSeqId < inputEntry.FirstSeqId - 1)
            {
                LogFireSkip($"floor not ready: currentFloor={currentFloorSeqId} targetSeqID={inputEntry.FirstSeqId} clockTime={clockSeconds:F6}s");
                suppressNextAdaptiveCorrection = true;
                break;
            }

            if (currentFloorSeqId == 0 && inputEntry.FirstSeqId == 1 && settings.FirstHitMode == FirstHitMode.Manual)
            {
                LogFireSkip($"manual first hit waiting: currentFloor={currentFloorSeqId} targetSeqID={inputEntry.FirstSeqId} clockTime={clockSeconds:F6}s");
                break;
            }

            if (currentFloorSeqId == 0 && inputEntry.FirstSeqId == 1 && settings.FirstHitMode == FirstHitMode.InputPatch)
            {
                if (!firstInputPatchScheduled)
                {
                    ScheduleFirstInputPatch(clockSeconds, currentFloorSeqId, inputEntry);
                }
                else
                {
                    LogFireSkip($"InputPatch first hit waiting: currentFloor={currentFloorSeqId} targetSeqID={inputEntry.FirstSeqId} clockTime={clockSeconds:F6}s");
                }

                break;
            }

            if (diffMs > settings.MaxLateRetryMs)
            {
                suppressNextAdaptiveCorrection = true;
                if (HandleHitFailedTooLate(entry, clockSeconds, diffMs, "entry too late before hit"))
                {
                    if (!running)
                    {
                        return;
                    }

                    continue;
                }

                break;
            }

            LogVerbose($"Fire attempt: seqID={entry.SeqId} targetTime={effectiveTargetTimeSeconds:F6}s baseTargetTime={entry.TargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} currentFloor={currentFloorSeqId} mode={settings.ClockMode} fireMode={settings.FireMode} adaptiveOffsetMs={adaptiveOffsetMs:F3}");

            if (settings.DryRun)
            {
                LogNormal($"DryRun targetTime={effectiveTargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} seqID={inputEntry.FirstSeqId}-{inputEntry.LastSeqId} rawEntryCount={inputEntry.RawEntryCount} emittedHitCount={inputEntry.EmittedHitCount} currFloorSeqID={currentFloorSeqId} clockMode={settings.ClockMode}");
                nextIndex++;
                break;
            }

            if (settings.FireMode == FireMode.HitInputEvent)
            {
                if (!allowDirectHit)
                {
                    return;
                }

                HitInvokeResult result = hitInputEventInvoker.Invoke(entry.SeqId, clockSeconds);
                LogHitResult(currentFloorSeqId, result);
                if (result.ShouldConsume)
                {
                    RecordHitDiff(diffMs);
                    nextIndex++;
                    break;
                }

                LogNormal($"Hit failed; keeping nextIndex={nextIndex} seqID={entry.SeqId}");
                suppressNextAdaptiveCorrection = true;
                if (HandleHitFailedTooLate(entry, clockSeconds, diffMs, "HitInputEvent did not advance floor"))
                {
                    if (!running)
                    {
                        return;
                    }

                    continue;
                }

                break;
            }

            if (settings.FireMode == FireMode.DirectHit)
            {
                if (!allowDirectHit)
                {
                    return;
                }

                if (inputEntry.IsPseudoChordGroup)
                {
                    if (TryFirePseudoChordGroup(inputEntry, clockSeconds, effectiveTargetTimeSeconds, diffMs, currentFloorSeqId, dueCount, out int afterGroupFloorSeqId))
                    {
                        UpdateAdaptiveOffsetAfterDirectHit(diffMs, Math.Max(dueCount, inputEntry.RawEntryCount), entry);
                        nextIndex++;
                        if (afterGroupFloorSeqId > inputEntry.LastSeqId)
                        {
                            nextIndex = AdvanceIndexPastCurrentFloor(nextIndex, afterGroupFloorSeqId);
                        }

                        hitsThisUpdate++;
                        if (!settings.EnableHighDensityMode)
                        {
                            break;
                        }

                        if (hitsThisUpdate >= maxHitsThisUpdate)
                        {
                            LogVerbose($"highDensity maxHitsReached hitsThisUpdate={hitsThisUpdate} maxHitsPerUpdate={maxHitsThisUpdate} nextIndex={nextIndex} dueCount={dueCount}");
                            break;
                        }

                        continue;
                    }

                    LogNormal($"Hit failed; keeping nextIndex={nextIndex} seqID={inputEntry.FirstSeqId}-{inputEntry.LastSeqId}");
                    suppressNextAdaptiveCorrection = true;
                    if (HandleHitFailedTooLate(inputEntry, clockSeconds, diffMs, "DirectHit pseudo chord group did not complete"))
                    {
                        if (!running)
                        {
                            return;
                        }

                        continue;
                    }

                    break;
                }

                int beforeFloorSeqId = currentFloorSeqId;
                HitInvokeResult result = directHitInvoker.Invoke(
                    entry.SeqId,
                    clockSeconds,
                    beforeFloorSeqId,
                    effectiveTargetTimeSeconds,
                    forceReadAfterHit: entry.IsMidspin || entry.IsNearMidspin);
                LogHitResult(currentFloorSeqId, result);
                int afterFloorSeqId = result.AfterFloorSeqId;
                if (afterFloorSeqId < 0 &&
                    TryReadCurrentFloorSeqId(out int verifiedAfterFloorSeqId))
                {
                    afterFloorSeqId = verifiedAfterFloorSeqId;
                }

                bool shouldConsumeDirectHit = result.Accepted && afterFloorSeqId >= entry.SeqId;
                if (shouldConsumeDirectHit)
                {
                    RecordHitDiff(diffMs);
                    UpdateAdaptiveOffsetAfterDirectHit(diffMs, dueCount, entry);
                    nextIndex++;
                    hitsThisUpdate++;
                    PulseMacroKeyViewer();
                    if (!settings.EnableHighDensityMode)
                    {
                        break;
                    }

                    if (hitsThisUpdate >= maxHitsThisUpdate)
                    {
                        LogVerbose($"highDensity maxHitsReached hitsThisUpdate={hitsThisUpdate} maxHitsPerUpdate={maxHitsThisUpdate} nextIndex={nextIndex} dueCount={dueCount}");
                        break;
                    }

                    continue;
                }

                if (result.Accepted)
                {
                    LogVerbose($"DirectHit accepted but floor not advanced to target. currentFloor={currentFloorSeqId} targetSeqID={entry.SeqId} afterFloor={afterFloorSeqId}");
                    suppressNextAdaptiveCorrection = true;
                    break;
                }

                LogNormal($"Hit failed; keeping nextIndex={nextIndex} seqID={entry.SeqId}");
                suppressNextAdaptiveCorrection = true;
                if (HandleHitFailedTooLate(entry, clockSeconds, diffMs, "DirectHit did not advance floor"))
                {
                    if (!running)
                    {
                        return;
                    }

                    continue;
                }

                break;
            }

            if (!allowInputPatch)
            {
                return;
            }

            int virtualInputCount = Math.Max(1, settings.VirtualInputKeyCount);
            InputPatchState.BeginFrame(virtualInputCount);
            LogNormal($"InputPatch mode does not confirm floor advancement. scheduled count=1 virtualKey={settings.VirtualInputKey} virtualKeyCount={virtualInputCount} clockTime={clockSeconds:F6}s currFloorSeqID={currentFloorSeqId} seqID={inputEntry.FirstSeqId}-{inputEntry.LastSeqId}");
            nextIndex++;
            break;
        }

        if (settings.FireMode == FireMode.DirectHit &&
            settings.EnableHighDensityMode &&
            hitsThisUpdate > 0)
        {
            LogVerbose($"highDensity hitsThisUpdate={hitsThisUpdate} maxHitsPerUpdate={maxHitsThisUpdate} dueCount={dueCount} nextIndex={nextIndex}");
        }
    }

    private int GetMaxHitsPerPlayerControlUpdate()
    {
        return settings.EnableHighDensityMode
            ? Math.Max(1, settings.MaxHitsPerPlayerControlUpdate)
            : 1;
    }

    private bool TryFirePseudoChordGroup(
        InputPlanEntry entry,
        double clockSeconds,
        double effectiveTargetTimeSeconds,
        double diffMs,
        int currentFloorBefore,
        int dueCount,
        out int currentFloorAfter)
    {
        currentFloorAfter = currentFloorBefore;
        int acceptedHitCount = 0;
        bool completed = true;

        for (int hitIndex = 0; hitIndex < entry.EmittedHitCount; hitIndex++)
        {
            int beforeFloorSeqId = currentFloorAfter;
            if (!TryReadCurrentFloorSeqId(out beforeFloorSeqId))
            {
                beforeFloorSeqId = currentFloorAfter;
            }

            if (beforeFloorSeqId >= entry.LastSeqId)
            {
                break;
            }

            int targetSeqId = Math.Min(beforeFloorSeqId + 1, entry.LastSeqId);
            HitInvokeResult result = directHitInvoker.Invoke(
                targetSeqId,
                clockSeconds,
                beforeFloorSeqId,
                effectiveTargetTimeSeconds,
                forceReadAfterHit: true);
            LogHitResult(beforeFloorSeqId, result);

            currentFloorAfter = result.AfterFloorSeqId;
            if (currentFloorAfter < 0 &&
                TryReadCurrentFloorSeqId(out int verifiedAfterFloorSeqId))
            {
                currentFloorAfter = verifiedAfterFloorSeqId;
            }

            if (!result.Accepted)
            {
                completed = false;
                break;
            }

            acceptedHitCount++;
            RecordHitDiff(diffMs);
            PulseMacroKeyViewer();
        }

        LogNormal(
            $"pseudoChord compressed. groupStartIndex={entry.PlanStartIndex} groupEndIndex={entry.PlanEndIndexExclusive - 1} firstSeqID={entry.FirstSeqId} lastSeqID={entry.LastSeqId} seqID={entry.FirstSeqId}-{entry.LastSeqId} firstTargetTime={entry.FirstTargetTimeSeconds:F6}s lastTargetTime={entry.LastTargetTimeSeconds:F6}s rawEntryCount={entry.RawEntryCount} emittedHitCount={entry.EmittedHitCount} acceptedHitCount={acceptedHitCount} windowMs={settings.PseudoChordWindowMs:F3} currentFloorBefore={currentFloorBefore} currentFloorAfter={currentFloorAfter} dueCount={dueCount} containsMidspin={entry.ContainsMidspin}");

        return completed && (acceptedHitCount == entry.EmittedHitCount || currentFloorAfter >= entry.LastSeqId);
    }

    private int AdvanceIndexPastCurrentFloor(int startIndex, int currentFloorSeqId)
    {
        int index = startIndex;
        while (index < inputPlan.Count && inputPlan[index].LastSeqId <= currentFloorSeqId)
        {
            index++;
        }

        return index;
    }

    private void PulseMacroKeyViewer()
    {
        if (!settings.EnableMacroKeyViewer)
        {
            return;
        }

        IReadOnlyList<string> keys = MacroKeyViewer.ConfigureKeys(settings.MacroKeyViewerKeysText);
        if (keys.Count == 0)
        {
            return;
        }

        int keyIndex = (int)(macroKeyViewerHitCounter % keys.Count);
        macroKeyViewerHitCounter++;
        double durationSeconds = Math.Max(0, settings.MacroKeyViewerPulseMs) / 1000.0;
        MacroKeyViewer.Pulse(keys[keyIndex], durationSeconds);
    }

    private bool HandleDueCountTooLarge(int dueCount, int maxHitsThisUpdate, double clockSeconds)
    {
        int threshold = Math.Max(
            MinDueBacklogFailureThreshold,
            Math.Max(1, maxHitsThisUpdate) * DueBacklogFailureMultiplier);
        if (dueCount <= threshold)
        {
            return false;
        }

        suppressNextAdaptiveCorrection = true;
        if (settings.FailureMode == FailureMode.Skip)
        {
            int skipCount = Math.Min(dueCount - threshold, inputPlan.Count - nextIndex);
            log($"dueCount too large; skipping backlog. dueCount={dueCount} threshold={threshold} skipCount={skipCount} maxHitsPerUpdate={maxHitsThisUpdate} clockTime={clockSeconds:F6}s");
            nextIndex += skipCount;
            return true;
        }

        log($"dueCount too large; stopping. dueCount={dueCount} threshold={threshold} maxHitsPerUpdate={maxHitsThisUpdate} clockTime={clockSeconds:F6}s");
        Stop("due count too large");
        return true;
    }

    private int CountDueEntries(double clockSeconds)
    {
        int count = 0;
        int index = nextIndex;
        while (index < inputPlan.Count && GetEffectiveTargetTimeSeconds(inputPlan[index]) <= clockSeconds)
        {
            count++;
            index++;
        }

        return count;
    }

    private double GetEffectiveTargetTimeSeconds(MacroPlanEntry entry)
    {
        double adaptiveSeconds = settings.EnableAdaptiveOffset ? adaptiveOffsetMs / 1000.0 : 0.0;
        return entry.TargetTimeSeconds + adaptiveSeconds;
    }

    private double GetEffectiveTargetTimeSeconds(InputPlanEntry entry)
    {
        double adaptiveSeconds = settings.EnableAdaptiveOffset ? adaptiveOffsetMs / 1000.0 : 0.0;
        return entry.FirstTargetTimeSeconds + adaptiveSeconds;
    }

    private void LogHitResult(int currentFloorSeqId, HitInvokeResult result)
    {
        string message = $"Hit result currentFloor={currentFloorSeqId} targetSeqID={result.TargetSeqId} accepted={result.Accepted} immediateAdvanced={result.ImmediateAdvanced} atOrPastTarget={result.AtOrPastTarget} shouldConsume={result.ShouldConsume} beforeFloor={result.BeforeFloorSeqId} afterFloor={result.AfterFloorSeqId}";
        if (settings.ValidateAfterHit)
        {
            LogNormal(message);
        }
        else
        {
            LogVerbose(message);
        }
    }

    private void RecordHitDiff(double diffMs)
    {
        hitDiffSampleCount++;
        hitDiffTotalMs += diffMs;
        hitDiffMaxAbsMs = Math.Max(hitDiffMaxAbsMs, Math.Abs(diffMs));
    }

    private void ResetHitDiffStats()
    {
        hitDiffSampleCount = 0;
        hitDiffTotalMs = 0.0;
        hitDiffMaxAbsMs = 0.0;
        recentDirectHitDiffMs.Clear();
        medianDispatchLagMs = 0.0;
    }

    private void ResetAdaptiveOffset()
    {
        adaptiveOffsetMs = 0.0;
        suppressNextAdaptiveCorrection = false;
    }

    private void UpdateAdaptiveOffsetAfterDirectHit(double diffMs, int dueCount, MacroPlanEntry entry)
    {
        if (!settings.EnableAdaptiveOffset)
        {
            return;
        }

        if (dueCount > 1 ||
            entry.IsMidspin ||
            entry.IsNearMidspin ||
            suppressNextAdaptiveCorrection)
        {
            suppressNextAdaptiveCorrection = false;
            return;
        }

        recentDirectHitDiffMs.Enqueue(diffMs);
        while (recentDirectHitDiffMs.Count > AdaptiveDiffWindowSize)
        {
            recentDirectHitDiffMs.Dequeue();
        }

        medianDispatchLagMs = CalculateMedian(recentDirectHitDiffMs);

        double targetAdaptiveOffsetMs = -medianDispatchLagMs;
        double nextAdaptiveOffsetMs = adaptiveOffsetMs +
                                      (targetAdaptiveOffsetMs - adaptiveOffsetMs) * AdaptiveLerpFactor;
        adaptiveOffsetMs = Math.Max(
            -AdaptiveMaxAbsMs,
            Math.Min(AdaptiveMaxAbsMs, nextAdaptiveOffsetMs));
        LogVerbose($"AdaptiveOffset dispatch-lag updated. medianDispatchLagMs={medianDispatchLagMs:F3} targetAdaptiveOffsetMs={targetAdaptiveOffsetMs:F3} adaptiveOffsetMs={adaptiveOffsetMs:F3} effectiveOffsetMs={EffectiveOffsetMs:F3}");
    }

    private static double CalculateMedian(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(value => value).ToArray();
        if (sorted.Length == 0)
        {
            return 0.0;
        }

        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) * 0.5
            : sorted[middle];
    }

    private bool HandleHitFailedTooLate(MacroPlanEntry entry, double clockSeconds, double diffMs, string reason)
    {
        if (diffMs <= settings.MaxLateRetryMs)
        {
            return false;
        }

        suppressNextAdaptiveCorrection = true;
        if (settings.FailureMode == FailureMode.Skip)
        {
            log($"tooLateSkipped: reason={reason} seqID={entry.SeqId} targetTime={entry.TargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} maxLateRetryMs={settings.MaxLateRetryMs:F3}");
            nextIndex++;
            return true;
        }

        log($"tooLateStopped: reason={reason} seqID={entry.SeqId} targetTime={entry.TargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} maxLateRetryMs={settings.MaxLateRetryMs:F3}");
        Stop("hit failed too late");
        return true;
    }

    private bool HandleHitFailedTooLate(InputPlanEntry entry, double clockSeconds, double diffMs, string reason)
    {
        if (diffMs <= settings.MaxLateRetryMs)
        {
            return false;
        }

        suppressNextAdaptiveCorrection = true;
        if (settings.FailureMode == FailureMode.Skip)
        {
            log($"tooLateSkipped: reason={reason} seqID={entry.FirstSeqId}-{entry.LastSeqId} firstTargetTime={entry.FirstTargetTimeSeconds:F6}s lastTargetTime={entry.LastTargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} maxLateRetryMs={settings.MaxLateRetryMs:F3}");
            nextIndex++;
            return true;
        }

        log($"tooLateStopped: reason={reason} seqID={entry.FirstSeqId}-{entry.LastSeqId} firstTargetTime={entry.FirstTargetTimeSeconds:F6}s lastTargetTime={entry.LastTargetTimeSeconds:F6}s clockTime={clockSeconds:F6}s diffMs={diffMs:F3} maxLateRetryMs={settings.MaxLateRetryMs:F3}");
        Stop("hit failed too late");
        return true;
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
        LogNormal($"waiting for clock reset: currentFloor={currentFloor} firstTargetTime={firstTargetTime:F6}s clockTime={clockSeconds:F6}s");
    }

    private void LogPlayerControlUpdateTick()
    {
        if (Time.unscaledTime - lastPlayerControlTickLogTime < PlayerControlTickLogIntervalSeconds)
        {
            return;
        }

        lastPlayerControlTickLogTime = Time.unscaledTime;
        LogVerbose($"PlayerControl_Update tick received. armed={armed} running={running}");
    }

    private void LogFireSkip(string reason)
    {
        if (Time.unscaledTime - lastFireSkipLogTime < FireSkipLogIntervalSeconds)
        {
            return;
        }

        lastFireSkipLogTime = Time.unscaledTime;
        LogVerbose($"FireDueInputs skipped: {reason}");
    }

    private void LogClockLost()
    {
        if (Time.unscaledTime - lastClockLostLogTime < ClockLostLogIntervalSeconds)
        {
            return;
        }

        lastClockLostLogTime = Time.unscaledTime;
        LogNormal($"clock lost: mode={settings.ClockMode}");
    }

    private void LogCurrentFloorLost()
    {
        if (Time.unscaledTime - lastCurrentFloorLostLogTime < CurrentFloorLostLogIntervalSeconds)
        {
            return;
        }

        lastCurrentFloorLostLogTime = Time.unscaledTime;
        LogNormal("Current currFloor.seqID was not available. Keeping internal macro scheduler running.");
    }

    private void LogNormal(string message)
    {
        if (settings.LoggingMode >= LoggingMode.Normal)
        {
            log(message);
        }
    }

    private void LogVerbose(string message)
    {
        if (settings.LoggingMode == LoggingMode.Verbose)
        {
            log(message);
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
