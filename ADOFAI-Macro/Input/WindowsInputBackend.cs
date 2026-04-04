using System.ComponentModel;
using System.Runtime.InteropServices;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Input;

public sealed class WindowsInputBackend : IInputBackend
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public void KeyDown(FingerKey key)
    {
        SendKey(MapVk(key), false);
    }

    public void KeyUp(FingerKey key)
    {
        SendKey(MapVk(key), true);
    }

    private static void SendKey(ushort vk, bool keyUp)
    {
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

        INPUT[] inputs =
        [
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = scan,
                        dwFlags = KEYEVENTF_SCANCODE | (keyUp ? KEYEVENTF_KEYUP : 0),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        ];

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error,
                $"SendInput failed. sent={sent}, err={error}, vk=0x{vk:X2}, scan=0x{scan:X2}");
        }
    }

    private static ushort MapVk(FingerKey key)
    {
        return key switch
        {
            FingerKey.Tab => VirtualKeys.TAB,
            FingerKey.D1 => VirtualKeys.D1,
            FingerKey.D2 => VirtualKeys.D2,
            FingerKey.E => VirtualKeys.E,
            FingerKey.P => VirtualKeys.P,
            FingerKey.Caret => VirtualKeys.OEM_7,
            FingerKey.Backslash => VirtualKeys.OEM_5,
            FingerKey.Enter => VirtualKeys.ENTER,
            FingerKey.C => VirtualKeys.C,
            FingerKey.Period => VirtualKeys.PERIOD,
            FingerKey.LeftShift => VirtualKeys.LEFT_SHIFT,
            FingerKey.RightShift => VirtualKeys.RIGHT_SHIFT,
            FingerKey.LeftControl => VirtualKeys.LEFT_CONTROL,
            FingerKey.RightControl => VirtualKeys.RIGHT_CONTROL,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
    }
}