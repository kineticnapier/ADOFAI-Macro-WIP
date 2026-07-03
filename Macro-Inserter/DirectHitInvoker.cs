using System;
using System.Reflection;

namespace Macro_Inserter;

internal sealed class DirectHitInvoker
{
    private readonly InternalMacroSettings settings;
    private readonly Action<string> log;
    private MethodInfo? hitMethod;

    public DirectHitInvoker(InternalMacroSettings settings, Action<string> log)
    {
        this.settings = settings;
        this.log = log;
    }

    public void Warmup()
    {
        ReflectionCache.GetSingletonInstance("scrController");
        ReflectionCache.GetSingletonInstance("scrConductor");
        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        ReflectionCache.WarmupMembers("scrController", "currFloor", "currentFloor", "floor", "seqID", "currentFloorSeqID");
        ReflectionCache.WarmupMembers("scrConductor", "songposition", "songposition_minusi");
    }

    public HitInvokeResult Invoke(
        int seqId,
        double audioTime,
        int beforeFloorSeqId,
        double targetTimeSeconds,
        bool? ignoreInputOverride = null)
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            LogNormal("scrController.instance was not found for DirectHit.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        hitMethod ??= ReflectionCache.FindMethod("scrController", "Hit", typeof(bool));
        if (hitMethod == null)
        {
            LogNormal("scrController.Hit(bool) was not found.");
            return CreateResult(false, beforeFloorSeqId, -1, seqId);
        }

        object? result;
        TimeSpoofState? timeSpoofState = null;
        bool ignoreInput = ignoreInputOverride ?? settings.DirectHitIgnoreInput;
        try
        {
            timeSpoofState = BeginTimeSpoof(targetTimeSeconds);
            result = hitMethod.Invoke(controller, new object[] { ignoreInput });
        }
        catch (Exception ex)
        {
            LogInvalidFloorIfNeeded(seqId, audioTime);
            LogNormal($"DirectHit threw {ex.GetType().Name}. seqID={seqId} audioTime={audioTime:F6}s.");
            return CreateResult(false, beforeFloorSeqId, ReadCurrentFloorSeqIdIfNeeded(), seqId);
        }
        finally
        {
            timeSpoofState?.Restore();
        }

        if (result is bool accepted)
        {
            LogVerbose($"DirectHit result={accepted} ignoreInput={ignoreInput} seqID={seqId} audioTime={audioTime:F6}s");
            if (!accepted)
            {
                LogInvalidFloorIfNeeded(seqId, audioTime);
                LogNormal($"DirectHit failed. seqID={seqId} audioTime={audioTime:F6}s.");
            }

            if (!settings.ValidateAfterHit)
            {
                return new HitInvokeResult(
                    accepted,
                    false,
                    accepted,
                    accepted,
                    beforeFloorSeqId,
                    -1,
                    seqId);
            }

            return CreateResult(accepted, beforeFloorSeqId, ReadCurrentFloorSeqIdIfNeeded(), seqId);
        }

        return CreateResult(false, beforeFloorSeqId, ReadCurrentFloorSeqIdIfNeeded(), seqId);
    }

    private TimeSpoofState? BeginTimeSpoof(double targetTimeSeconds)
    {
        if (!settings.ExperimentalTimeSpoofForDirectHit)
        {
            return null;
        }

        try
        {
            object? conductor = ReflectionCache.GetSingletonInstance("scrConductor");
            if (conductor == null)
            {
                LogNormal("timeSpoof failed: scrConductor.instance was not found.");
                return null;
            }

            object? oldSongPosition = ReflectionCache.ReadMember(conductor, "songposition");
            object? oldSongPositionMinusI = ReflectionCache.ReadMember(conductor, "songposition_minusi");
            bool wroteSongPosition = ReflectionCache.WriteMember(conductor, targetTimeSeconds, "songposition");
            bool wroteSongPositionMinusI = ReflectionCache.WriteMember(conductor, targetTimeSeconds, "songposition_minusi");
            if (!wroteSongPosition && !wroteSongPositionMinusI)
            {
                LogNormal("timeSpoof failed: conductor songposition fields were not writable.");
                return null;
            }

            LogVerbose($"timeSpoof enabled targetTime={targetTimeSeconds:F6}s wroteSongposition={wroteSongPosition} wroteSongpositionMinusI={wroteSongPositionMinusI}");
            return new TimeSpoofState(
                conductor,
                oldSongPosition,
                oldSongPositionMinusI,
                wroteSongPosition,
                wroteSongPositionMinusI,
                message => LogVerbose(message));
        }
        catch (Exception ex)
        {
            LogNormal($"timeSpoof failed: {ex.GetType().Name}.");
            return null;
        }
    }

    private void LogInvalidFloorIfNeeded(int seqId, double audioTime)
    {
        if (seqId == 0)
        {
            LogNormal($"DirectHit failed because invalid floor 0 was scheduled. audioTime={audioTime:F6}s.");
        }
    }

    private static HitInvokeResult CreateResult(bool accepted, int beforeFloorSeqId, int afterFloorSeqId, int targetSeqId)
    {
        bool immediateAdvanced = afterFloorSeqId > beforeFloorSeqId;
        bool atOrPastTarget = afterFloorSeqId >= targetSeqId;
        bool shouldConsume = accepted && atOrPastTarget;
        return new HitInvokeResult(
            accepted,
            immediateAdvanced,
            atOrPastTarget,
            shouldConsume,
            beforeFloorSeqId,
            afterFloorSeqId,
            targetSeqId);
    }

    private int ReadCurrentFloorSeqIdIfNeeded()
    {
        return settings.ValidateAfterHit ? ReadCurrentFloorSeqId() : -1;
    }

    private static int ReadCurrentFloorSeqId()
    {
        object? controller = ReflectionCache.GetSingletonInstance("scrController");
        if (controller == null)
        {
            return -1;
        }

        object? currFloor = ReflectionCache.ReadMember(controller, "currFloor", "currentFloor");
        if (currFloor == null)
        {
            return ReflectionCache.TryReadInt(controller, out int controllerSeqId, "floor", "seqID", "currentFloorSeqID")
                ? controllerSeqId
                : -1;
        }

        if (currFloor is int intValue)
        {
            return intValue;
        }

        return ReflectionCache.TryReadInt(currFloor, out int seqId, "seqID", "seqId", "floorSeqID")
            ? seqId
            : -1;
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

    private sealed class TimeSpoofState
    {
        private readonly object conductor;
        private readonly object? oldSongPosition;
        private readonly object? oldSongPositionMinusI;
        private readonly bool restoreSongPosition;
        private readonly bool restoreSongPositionMinusI;
        private readonly Action<string> logVerbose;

        public TimeSpoofState(
            object conductor,
            object? oldSongPosition,
            object? oldSongPositionMinusI,
            bool restoreSongPosition,
            bool restoreSongPositionMinusI,
            Action<string> logVerbose)
        {
            this.conductor = conductor;
            this.oldSongPosition = oldSongPosition;
            this.oldSongPositionMinusI = oldSongPositionMinusI;
            this.restoreSongPosition = restoreSongPosition;
            this.restoreSongPositionMinusI = restoreSongPositionMinusI;
            this.logVerbose = logVerbose;
        }

        public void Restore()
        {
            bool restoredSongPosition = !restoreSongPosition ||
                                        ReflectionCache.WriteMember(conductor, oldSongPosition, "songposition");
            bool restoredSongPositionMinusI = !restoreSongPositionMinusI ||
                                              ReflectionCache.WriteMember(conductor, oldSongPositionMinusI, "songposition_minusi");
            if (!restoredSongPosition || !restoredSongPositionMinusI)
            {
                logVerbose($"timeSpoof failed to restore fully. restoredSongposition={restoredSongPosition} restoredSongpositionMinusI={restoredSongPositionMinusI}");
            }
        }
    }
}
