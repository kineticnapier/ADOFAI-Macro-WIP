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
        SendKey(key, false);
    }

    public void KeyUp(FingerKey key)
    {
        SendKey(key, true);
    }

    private static void SendKey(FingerKey key, bool keyUp)
    {
        ushort vk = MapVk(key);
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

        uint flags = KEYEVENTF_SCANCODE;

        if (IsExtendedKey(key))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        if (keyUp)
        {
            flags |= KEYEVENTF_KEYUP;
        }

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
                        dwFlags = flags,
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

    private static bool IsExtendedKey(FingerKey key)
    {
        return key switch
        {
            FingerKey.RightControl => true,
            // 必要なら他にも追加
            // FingerKey.Enter => true,  // テンキーEnterなら true だが通常Enterとは別扱い注意
            _ => false
        };
    }

    private static ushort MapVk(FingerKey key)
    {
        return key switch
        {
            FingerKey.Tab => VirtualKeys.TAB,
            FingerKey.D1 => VirtualKeys.D1,
            FingerKey.D2 => VirtualKeys.D2,
            FingerKey.D3 => VirtualKeys.D3,
            FingerKey.D4 => VirtualKeys.D4,
            FingerKey.D5 => VirtualKeys.D5,
            FingerKey.D6 => VirtualKeys.D6,
            FingerKey.D7 => VirtualKeys.D7,
            FingerKey.D8 => VirtualKeys.D8,
            FingerKey.D9 => VirtualKeys.D9,
            FingerKey.D0 => VirtualKeys.D0,
            FingerKey.Caret => VirtualKeys.OEM_7,
            FingerKey.Backslash => VirtualKeys.OEM_5,
            FingerKey.Enter => VirtualKeys.ENTER,
            FingerKey.Period => VirtualKeys.PERIOD,
            FingerKey.LeftShift => VirtualKeys.LEFT_SHIFT,
            FingerKey.RightShift => VirtualKeys.RIGHT_SHIFT,
            FingerKey.LeftControl => VirtualKeys.LEFT_CONTROL,
            FingerKey.RightControl => VirtualKeys.RIGHT_CONTROL,
            FingerKey.A => VirtualKeys.A, //脳筋
            FingerKey.B => VirtualKeys.B,
            FingerKey.C => VirtualKeys.C,
            FingerKey.D => VirtualKeys.D,
            FingerKey.E => VirtualKeys.E,
            FingerKey.F => VirtualKeys.F,
            FingerKey.G => VirtualKeys.G,
            FingerKey.H => VirtualKeys.H,
            FingerKey.I => VirtualKeys.I,
            FingerKey.J => VirtualKeys.J,
            FingerKey.K => VirtualKeys.K,
            FingerKey.L => VirtualKeys.L,
            FingerKey.M => VirtualKeys.M,
            FingerKey.N => VirtualKeys.N,
            FingerKey.O => VirtualKeys.O,
            FingerKey.P => VirtualKeys.P,
            FingerKey.Q => VirtualKeys.Q,
            FingerKey.R => VirtualKeys.R,
            FingerKey.S => VirtualKeys.S,
            FingerKey.T => VirtualKeys.T,
            FingerKey.U => VirtualKeys.U,
            FingerKey.V => VirtualKeys.V,
            FingerKey.W => VirtualKeys.W,
            FingerKey.X => VirtualKeys.X,
            FingerKey.Y => VirtualKeys.Y,
            FingerKey.Z => VirtualKeys.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
    }
}