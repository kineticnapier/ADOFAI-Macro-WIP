using System.Diagnostics;

using ADOFAI_Macro.Input;
using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Scheduling;

public sealed class InputScheduler
{
    private readonly IInputBackend _backend;

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

        foreach (ScheduledInputEvent ev in events)
        {
            long absoluteTarget = baseTick + ev.TargetTick;

            while (true)
            {
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
}