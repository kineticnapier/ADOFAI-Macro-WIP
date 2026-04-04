using System.Diagnostics;
using System.Runtime.InteropServices;

using ADOFAI_Macro.Input;
using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Scheduling;

public sealed class InputScheduler
{
    private readonly IInputBackend _backend;

    private long _manualOffsetTick = 0;

    private const double OffsetStepMs = 5.0;

    private bool _leftWasDown = false;
    private bool _rightWasDown = false;

    public InputScheduler(IInputBackend backend)
    {
        _backend = backend;
    }

    public void PlayEventsFromBaseTick(
        IReadOnlyList<ScheduledInputEvent> events,
        long baseTick)
    {
        long frequency = Stopwatch.Frequency;
        long spinThreshold = (long)(frequency * 0.0003);
        long offsetStepTick = MsToTicks(OffsetStepMs, frequency);

        foreach (ScheduledInputEvent ev in events)
        {
            while (true)
            {
                HandleManualOffsetInput(offsetStepTick);

                long absoluteTarget = baseTick + ev.TargetTick + _manualOffsetTick;

                long now = Stopwatch.GetTimestamp();
                long remain = absoluteTarget - now;

                if (remain <= 0)
                {
                    break;
                }

                if (remain > spinThreshold)
                {
                    Thread.SpinWait(64);
                }
                else
                {
                    Thread.SpinWait(16);
                }
            }

            if (ev.Type == InputEventType.KeyDown)
            {
                _backend.KeyDown(ev.Key);
                Console.WriteLine($"KeyDown: {ev.Key} at tick {ev.TargetTick}");
            }
            else
            {
                _backend.KeyUp(ev.Key);
            }
        }
    }

    private void HandleManualOffsetInput(long offsetStepTick)
    {
        bool leftDown = IsKeyDown(VK_LEFT);
        bool rightDown = IsKeyDown(VK_RIGHT);

        if (leftDown && !_leftWasDown)
        {
            _manualOffsetTick -= offsetStepTick;
            Console.Title = $"Offset = {TicksToMs(_manualOffsetTick, Stopwatch.Frequency):F2} ms";
        }

        if (rightDown && !_rightWasDown)
        {
            _manualOffsetTick += offsetStepTick;
            Console.Title = $"Offset = {TicksToMs(_manualOffsetTick, Stopwatch.Frequency):F2} ms";
        }

        _leftWasDown = leftDown;
        _rightWasDown = rightDown;
    }
    
    private static long MsToTicks(double ms, long frequency)
    {
        return (long)Math.Round(ms * frequency / 1000.0);
    }

    private static double TicksToMs(long ticks, long frequency)
    {
        return ticks * 1000.0 / frequency;
    }

    private const int VK_LEFT = 0x25;
    private const int VK_RIGHT = 0x27;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsKeyDown(int vKey)
    {
        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }
}