using System.Runtime.InteropServices;

namespace KawaiRun2Launcher.Controller;

internal static class Native
{

    internal const int INPUT_MOUSE = 0;
    internal const int INPUT_KEYBOARD = 1;

    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_SCANCODE = 0x0008;

    internal const uint MOUSEEVENTF_MOVE = 0x0001;
    internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
    internal const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    internal const uint MAPVK_VK_TO_VSC = 0;

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_LBUTTONUP = 0x0202;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, uint uFlags);

    internal const uint CWP_SKIPINVISIBLE = 0x0001;
    internal const uint CWP_SKIPDISABLED = 0x0002;
    internal const uint CWP_SKIPTRANSPARENT = 0x0004;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern bool SetCursorPos(int x, int y);

    internal static readonly IntPtr DPI_AWARENESS_CONTEXT_UNAWARE = new(-1);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint WM_HOTKEY = 0x0312;

    internal const int JobObjectExtendedLimitInformation = 9;
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    internal static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    internal static extern uint TimeEndPeriod(uint uMilliseconds);

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    internal const short XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE = 7849;
    internal const short XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE = 8689;
    internal const byte XINPUT_GAMEPAD_TRIGGER_THRESHOLD = 30;

    internal const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
    internal const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
    internal const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
    internal const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
    internal const ushort XINPUT_GAMEPAD_START = 0x0010;
    internal const ushort XINPUT_GAMEPAD_BACK = 0x0020;
    internal const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
    internal const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
    internal const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
    internal const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    internal const ushort XINPUT_GAMEPAD_A = 0x1000;
    internal const ushort XINPUT_GAMEPAD_B = 0x2000;
    internal const ushort XINPUT_GAMEPAD_X = 0x4000;
    internal const ushort XINPUT_GAMEPAD_Y = 0x8000;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    internal static extern int XInputGetState14(uint dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    internal static extern int XInputGetState910(uint dwUserIndex, out XINPUT_STATE pState);

    internal static int XInputGetState(uint dwUserIndex, out XINPUT_STATE state)
    {
        try
        {
            return XInputGetState14(dwUserIndex, out state);
        }
        catch (DllNotFoundException)
        {
            return XInputGetState910(dwUserIndex, out state);
        }
        catch (EntryPointNotFoundException)
        {
            return XInputGetState910(dwUserIndex, out state);
        }
    }
}
