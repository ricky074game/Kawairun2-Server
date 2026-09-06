using System.Runtime.InteropServices;

namespace KawaiRun2Launcher.Controller;

internal readonly record struct VkDef(ushort Vk, bool Extended, bool Held);

internal static class VkTable
{
    public static readonly IReadOnlyDictionary<string, VkDef> Map = new Dictionary<string, VkDef>(StringComparer.Ordinal)
    {
        ["ARROW_UP"] = new VkDef(0x26, true, true),
        ["ARROW_DOWN"] = new VkDef(0x28, true, true),
        ["ARROW_LEFT"] = new VkDef(0x25, true, true),
        ["ARROW_RIGHT"] = new VkDef(0x27, true, true),
        ["W"] = new VkDef(0x57, false, true),
        ["A"] = new VkDef(0x41, false, true),
        ["S"] = new VkDef(0x53, false, true),
        ["D"] = new VkDef(0x44, false, true),
        ["SPACE"] = new VkDef(0x20, false, false),
        ["ENTER"] = new VkDef(0x0D, false, false),
        ["M"] = new VkDef(0x4D, false, false),
        ["P"] = new VkDef(0x50, false, false),
        ["Z"] = new VkDef(0x5A, false, false),
        ["X"] = new VkDef(0x58, false, false),
        ["C"] = new VkDef(0x43, false, false),

        ["BACKSPACE"] = new VkDef(0x08, false, false),
        ["TAB"] = new VkDef(0x09, false, false),
        ["F13"] = new VkDef(0x7C, false, false),
        ["F14"] = new VkDef(0x7D, false, false)
    };

    public static readonly IReadOnlyList<string> HeldTokens =
        Map.Where(kv => kv.Value.Held).Select(kv => kv.Key).ToList();
}

internal sealed class InputInjector
{
    private readonly Func<string> _injectionModeProvider;
    private int _projectorPid;
    private IntPtr _projectorHwnd;

    public InputInjector(Func<string> injectionModeProvider)
    {
        _injectionModeProvider = injectionModeProvider;
    }

    private bool UsePostMessage => string.Equals(_injectionModeProvider(), "postmessage", StringComparison.OrdinalIgnoreCase);

    public void SetProjector(int pid, IntPtr hwnd)
    {
        _projectorPid = pid;
        _projectorHwnd = hwnd;
    }

    public void RefreshProjectorHandle()
    {
        if (_projectorPid == 0)
        {
            return;
        }
        IntPtr resolved = FlashWindowCustomizer.FindMainWindow(_projectorPid);
        if (resolved != IntPtr.Zero)
        {
            _projectorHwnd = resolved;
        }
    }

    public IntPtr ProjectorHwnd => _projectorHwnd;

    public bool IsProjectorForeground()
    {
        if (_projectorPid == 0)
        {
            return false;
        }
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }
        Native.GetWindowThreadProcessId(fg, out uint pid);
        return pid == (uint)_projectorPid;
    }

    public void KeyDown(string token) => SendKeyEvent(token, down: true, force: false);

    public void KeyUp(string token, bool force = false) => SendKeyEvent(token, down: false, force);

    public void TapKey(string token)
    {
        SendKeyEvent(token, down: true, force: false);
        SendKeyEvent(token, down: false, force: false);
    }

    private void SendKeyEvent(string token, bool down, bool force)
    {
        if (!VkTable.Map.TryGetValue(token, out VkDef def))
        {
            return;
        }

        if (UsePostMessage)
        {
            PostKeyMessage(def, down);
            return;
        }

        if (!force && !IsProjectorForeground())
        {
            return;
        }

        SendScancodeInput(def, down);
    }

    private static void SendScancodeInput(VkDef def, bool down)
    {
        ushort scan = (ushort)Native.MapVirtualKeyW(def.Vk, Native.MAPVK_VK_TO_VSC);
        uint flags = Native.KEYEVENTF_SCANCODE
                     | (down ? 0u : Native.KEYEVENTF_KEYUP)
                     | (def.Extended ? Native.KEYEVENTF_EXTENDEDKEY : 0u);

        Native.INPUT input = new()
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
            }
        };
        Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.INPUT>());
    }

    private void PostKeyMessage(VkDef def, bool down)
    {
        if (_projectorHwnd == IntPtr.Zero)
        {
            return;
        }
        uint scan = Native.MapVirtualKeyW(def.Vk, Native.MAPVK_VK_TO_VSC);

        long bits = 1 | ((long)scan << 16) | (def.Extended ? 1L << 24 : 0);
        if (!down)
        {
            bits |= 1L << 30 | 1L << 31;
        }
        IntPtr lParam = unchecked((IntPtr)(int)(uint)bits);
        Native.PostMessageW(_projectorHwnd, down ? Native.WM_KEYDOWN : Native.WM_KEYUP, (IntPtr)def.Vk, lParam);
    }

    public Native.RECT? GetProjectorClientRectScreen()
    {
        if (_projectorHwnd == IntPtr.Zero)
        {
            return null;
        }

        IntPtr prevContext = Native.SetThreadDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_UNAWARE);
        try
        {
            if (!Native.GetClientRect(_projectorHwnd, out Native.RECT client))
            {
                return null;
            }
            Native.POINT topLeft = default;
            if (!Native.ClientToScreen(_projectorHwnd, ref topLeft))
            {
                return null;
            }
            return new Native.RECT
            {
                Left = topLeft.X,
                Top = topLeft.Y,
                Right = topLeft.X + client.Width,
                Bottom = topLeft.Y + client.Height
            };
        }
        finally
        {
            Native.SetThreadDpiAwarenessContext(prevContext);
        }
    }

    public Native.POINT? GetCursorScreenPos() => Native.GetCursorPos(out Native.POINT p) ? p : null;

    public void SetCursorScreenPos(int x, int y)
    {
        if (UsePostMessage)
        {
            return;
        }
        Native.SetCursorPos(x, y);
    }

    public void RecenterCursor()
    {
        Native.RECT? rect = GetProjectorClientRectScreen();
        if (rect is null || UsePostMessage)
        {
            return;
        }
        Native.SetCursorPos(rect.Value.Left + rect.Value.Width / 2, rect.Value.Top + rect.Value.Height / 2);
    }

    public void MouseDown()
    {
        if (UsePostMessage)
        {
            return;
        }
        if (!IsProjectorForeground())
        {
            return;
        }
        SendMouseButton(down: true);
    }

    public void MouseUp(bool force = false)
    {
        if (UsePostMessage)
        {
            return;
        }
        if (!force && !IsProjectorForeground())
        {
            return;
        }
        SendMouseButton(down: false);
    }

    private static void SendMouseButton(bool down)
    {
        Native.INPUT input = new()
        {
            type = Native.INPUT_MOUSE,
            U = new Native.InputUnion
            {
                mi = new Native.MOUSEINPUT
                {
                    dwFlags = down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP
                }
            }
        };
        Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.INPUT>());
    }

    public void PostMouseMove(int clientX, int clientY)
    {
        IntPtr target = ResolveTarget(clientX, clientY);
        if (target == IntPtr.Zero)
        {
            return;
        }
        Native.PostMessageW(target, Native.WM_MOUSEMOVE, IntPtr.Zero, MakePointLParam(clientX, clientY));
    }

    public void PostMouseButton(bool down, int clientX, int clientY)
    {
        IntPtr target = ResolveTarget(clientX, clientY);
        if (target == IntPtr.Zero)
        {
            return;
        }
        Native.PostMessageW(target, down ? Native.WM_LBUTTONDOWN : Native.WM_LBUTTONUP,
            (IntPtr)(down ? 1 : 0), MakePointLParam(clientX, clientY));
    }

    private IntPtr ResolveTarget(int clientX, int clientY)
    {
        if (_projectorHwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        Native.POINT pt = new() { X = clientX, Y = clientY };
        IntPtr child = Native.ChildWindowFromPointEx(_projectorHwnd, pt,
            Native.CWP_SKIPINVISIBLE | Native.CWP_SKIPDISABLED | Native.CWP_SKIPTRANSPARENT);
        return child != IntPtr.Zero ? child : _projectorHwnd;
    }

    private static IntPtr MakePointLParam(int x, int y) =>
        unchecked((IntPtr)(((short)y << 16) | (ushort)(short)x));

    public void ForceReleaseAllKnownKeys()
    {
        foreach (string token in VkTable.HeldTokens)
        {
            KeyUp(token, force: true);
        }
        MouseUp(force: true);
    }
}
