using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

using ADOFAI_Macro.Input;
using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Scheduling;

public sealed class InputScheduler(IInputBackend backend)
{
    private readonly IInputBackend _backend = backend;
    private long _manualOffsetTick = 0;

    public void PlayEventsFromBaseTick(
        IReadOnlyList<ScheduledInputEvent> events,
        long baseTick,
        CancellationToken cancellationToken = default)
    {
        IntPtr mmcssHandle = IntPtr.Zero;

        try
        {
            PreparePlaybackThread();
            mmcssHandle = EnterMmcss();

            long frequency = Stopwatch.Frequency;

            long dispatchAdvanceTick = MsToTicks(0.25, frequency);
            long sleep0Threshold = MsToTicks(2.0, frequency);
            long yieldThreshold = MsToTicks(0.5, frequency);
            long tightSpinThreshold = MsToTicks(0.10, frequency);

            long absoluteTarget = 0;

            foreach (ScheduledInputEvent ev in events)
            {
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    long offsetTick = Volatile.Read(ref _manualOffsetTick);
                    absoluteTarget = baseTick + ev.TargetTick + offsetTick - dispatchAdvanceTick;

                    long now = Stopwatch.GetTimestamp();
                    long remain = absoluteTarget - now;

                    if (remain <= 0)
                        break;

                    if (remain > sleep0Threshold)
                    {
                        Thread.Sleep(0);
                    }
                    else if (remain > yieldThreshold)
                    {
                        Thread.Yield();
                    }
                    else if (remain > tightSpinThreshold)
                    {
                        Thread.SpinWait(8);
                    }
                    else
                    {
                        while (Stopwatch.GetTimestamp() < absoluteTarget)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                return;
                        }
                        break;
                    }
                }

                if (ev.Type == InputEventType.KeyDown)
                {
                    _backend.KeyDown(ev.Key);
                }
                else
                    _backend.KeyUp(ev.Key);
            }
        }
        finally
        {
            if (mmcssHandle != IntPtr.Zero)
            {
                AvRevertMmThreadCharacteristics(mmcssHandle);
            }
        }
    }

    public void AddOffsetMs(double deltaMs)
    {
        long deltaTick = MsToTicks(deltaMs, Stopwatch.Frequency);
        Interlocked.Add(ref _manualOffsetTick, deltaTick);
    }

    private static long MsToTicks(double ms, long frequency)
    {
        return (long)Math.Round(ms * frequency / 1000.0);
    }

    public double GetOffsetMs()
    {
        long ticks = Volatile.Read(ref _manualOffsetTick);
        return TicksToMs(ticks, Stopwatch.Frequency);
    }
    private static double TicksToMs(long ticks, long frequency)
    {
        return ticks * 1000.0 / frequency;
    }

    private static void PreparePlaybackThread()
    {
        try
        {
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
        }
        catch
        {
        }

        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        catch
        {
        }

        try
        {
            IntPtr threadHandle = GetCurrentThread();
            SetThreadAffinityMask(threadHandle, new UIntPtr(1));
        }
        catch
        {
        }
    }

    private static IntPtr EnterMmcss()
    {
        uint taskIndex = 0;

        // "Games" か "Pro Audio" を試す
        IntPtr handle = AvSetMmThreadCharacteristics("Games", ref taskIndex);
        //if (handle != IntPtr.Zero)
        //{
        //    AvSetMmThreadPriority(handle, AVRT_PRIORITY.AVRT_PRIORITY_HIGH);
        //    return handle;
        //}

        handle = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);
        if (handle != IntPtr.Zero)
        {
            AvSetMmThreadPriority(handle, AVRT_PRIORITY.AVRT_PRIORITY_HIGH);
            return handle;
        }

        return IntPtr.Zero;
    }

    private enum AVRT_PRIORITY
    {
        AVRT_PRIORITY_LOW = -1,
        AVRT_PRIORITY_NORMAL,
        AVRT_PRIORITY_HIGH,
        AVRT_PRIORITY_CRITICAL
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr hThread, UIntPtr dwThreadAffinityMask);

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, AVRT_PRIORITY priority);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);
}