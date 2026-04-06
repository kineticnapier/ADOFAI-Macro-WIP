using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VKCodeViewer;

public class Program
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    static void Main()
    {
        Console.WriteLine("Press Key. (Press ESC to exit)");

        while (true)
        {
            for (int vk = 1; vk <= 255; vk++)
            {
                if ((GetAsyncKeyState(vk) & 0x0001) != 0)
                {
                    Console.WriteLine($"VK Code: 0x{vk:X2} ({vk})");

                    if (vk == 0x1B) return; // ESC
                }
            }

            Thread.Sleep(1);
        }
    }
}