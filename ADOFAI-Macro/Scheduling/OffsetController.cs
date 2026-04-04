using System.Runtime.InteropServices;
using System.Threading;

namespace ADOFAI_Macro.Scheduling;

public sealed class OffsetController(InputScheduler scheduler, double offsetStepMs = 1.0)
{
    private readonly InputScheduler _scheduler = scheduler;
    private readonly double _offsetStepMs = offsetStepMs;

    private bool _leftWasDown = false;
    private bool _rightWasDown = false;

    public void Run(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool leftDown = IsKeyDown(VK_LEFT);
            bool rightDown = IsKeyDown(VK_RIGHT);

            if (leftDown && !_leftWasDown)
            {
                _scheduler.AddOffsetMs(-_offsetStepMs);
                UpdateTitle();
            }

            if (rightDown && !_rightWasDown)
            {
                _scheduler.AddOffsetMs(+_offsetStepMs);
                UpdateTitle();
            }

            _leftWasDown = leftDown;
            _rightWasDown = rightDown;

            Thread.Sleep(1);
        }
    }

    private void UpdateTitle()
    {
        try
        {
            Console.Title = $"Offset = {_scheduler.GetOffsetMs():F2} ms";
        }
        catch
        {
        }
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