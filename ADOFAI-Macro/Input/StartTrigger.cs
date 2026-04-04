using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ADOFAI_Macro.Input;

public sealed class StartTrigger
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static long WaitForFirstPress(int virtualKey)
    {
        while (IsDown(virtualKey))
        {
            Thread.SpinWait(64);
        }

        while (true)
        {
            if (IsDown(virtualKey))
            {
                return Stopwatch.GetTimestamp();
            }

            Thread.SpinWait(64);
        }
    }

    private static bool IsDown(int virtualKey)
    {
        return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }
}