using System.Runtime.InteropServices;
using System.Threading;

using ADOFAI_Macro.Pico;

namespace ADOFAI_Macro.Scheduling;

public sealed class EscapeStopController(PicoSerialClient pico)
{
    private readonly PicoSerialClient _pico = pico;

    public void Run(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (IsKeyDown(VK_ESCAPE))
            {
                try
                {
                    _pico.Stop();
                }
                catch
                {
                }

                break;
            }

            Thread.Sleep(1);
        }
    }

    private const int VK_ESCAPE = 0x1B;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsKeyDown(int vKey)
    {
        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }
}