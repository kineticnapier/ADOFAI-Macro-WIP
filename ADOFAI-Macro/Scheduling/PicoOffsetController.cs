using System.Runtime.InteropServices;
using System.Threading;

using ADOFAI_Macro.Pico;

namespace ADOFAI_Macro.Scheduling;

public sealed class PicoOffsetController(PicoSerialClient pico, double offsetStepMs = 1.0)
{
    private readonly PicoSerialClient _pico = pico;
    private readonly double _offsetStepMs = offsetStepMs;

    private bool _leftWasDown = false;
    private bool _rightWasDown = false;

    private long _currentOffsetUs = 0;

    public void Run(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool leftDown = IsKeyDown(VK_LEFT);
            bool rightDown = IsKeyDown(VK_RIGHT);

            if (leftDown && !_leftWasDown)
            {
                long deltaUs = -(long)(_offsetStepMs * 1000.0);
                _pico.AddOffsetUs(deltaUs);
                _currentOffsetUs += deltaUs;
                UpdateTitle();
            }

            if (rightDown && !_rightWasDown)
            {
                long deltaUs = +(long)(_offsetStepMs * 1000.0);
                _pico.AddOffsetUs(deltaUs);
                _currentOffsetUs += deltaUs;
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
            Console.Title = $"Offset = {_currentOffsetUs / 1000.0:F2} ms";
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