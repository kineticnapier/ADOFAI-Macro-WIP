using System.ComponentModel;
using System.Runtime.InteropServices;

using ADOFAI_Macro.Models;

namespace ADOFAI_Macro.Input;

public sealed class WindowsInputBackend : IInputBackend
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

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
        INPUT[] inputs =
        [
            CreateKeyInput(vk, keyUp)
        ];

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error,
                $"SendInput failed. sent={sent}, err={error}, cbSize={Marshal.SizeOf<INPUT>()}, vk=0x{vk:X2}");
        }
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
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
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
    }
}