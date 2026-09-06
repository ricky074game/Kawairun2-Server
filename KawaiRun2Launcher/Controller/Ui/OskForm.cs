using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KawaiRun2Launcher.Controller.Ui;

internal static class OskKeyInjector
{
    private const uint KEYEVENTF_UNICODE = 0x0004;
    public const ushort VK_BACK = 0x08;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_SHIFT = 0x10;

    public static void SendChar(char c)
    {
        SendUnicode(c, keyUp: false);
        SendUnicode(c, keyUp: true);
    }

    private static void SendUnicode(char c, bool keyUp)
    {
        Native.INPUT input = new()
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion
            {
                ki = new Native.KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE | (keyUp ? Native.KEYEVENTF_KEYUP : 0u),
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.INPUT>());
    }

    public static void TapScancode(ushort vk)
    {
        SendScancode(vk, down: true);
        SendScancode(vk, down: false);
    }

    public static void SendScancodeKey(ushort vk, bool down) => SendScancode(vk, down);

    private static void SendScancode(ushort vk, bool down)
    {
        ushort scan = (ushort)Native.MapVirtualKeyW(vk, Native.MAPVK_VK_TO_VSC);
        uint flags = Native.KEYEVENTF_SCANCODE | (down ? 0u : Native.KEYEVENTF_KEYUP);
        Native.INPUT input = new()
        {
            type = Native.INPUT_KEYBOARD,
            U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } }
        };
        Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.INPUT>());
    }
}

internal sealed class OskForm : Form, IOskHost
{
    private const int CellSize = 42;
    private const int Cols = 10;
    private const int Rows = 4;

    private static readonly string[] RowsLower = { "1234567890", "qwertyuiop", "asdfghjkl\0", "zxcvbnm\0\0\0" };
    private static readonly string[] RowsUpper = { "1234567890", "QWERTYUIOP", "ASDFGHJKL\0", "ZXCVBNM\0\0\0" };
    private static readonly string[] RowsSymbols = { "!@#$%^&*()", "-_=+[]{}\\|", ";:'\",.<>/?", "`~\0\0\0\0\0\0\0\0" };

    private enum ShiftMode { Lower, Upper, Symbols }

    private readonly ControllerService _service;
    private readonly object _navLock = new();
    private readonly System.Windows.Forms.Timer _repositionTimer;
    private readonly Label[,] _cells = new Label[Rows, Cols];
    private readonly Label _spaceCell;
    private readonly Label _titleLabel;

    private volatile bool _isOpen;
    private int _row;
    private int _col;
    private ShiftMode _mode = ShiftMode.Lower;

    public bool IsOpen => _isOpen;

    public event Action? OpenBlockedByConfig;

    public OskForm(ControllerService service)
    {
        _service = service;

        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 26, 30);
        Opacity = 0.96;
        ClientSize = new Size(CellSize * Cols + 20, CellSize * (Rows + 1) + 46);

        _titleLabel = new Label
        {
            Text = "On-Screen Keyboard  (R3 / Start to close)",
            ForeColor = Color.Gainsboro,
            AutoSize = false,
            Height = 22,
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            Font = new Font("Segoe UI", 8f)
        };
        Controls.Add(_titleLabel);

        Panel grid = new() { Location = new Point(10, 26), Size = new Size(CellSize * Cols, CellSize * (Rows + 1)) };
        Controls.Add(grid);

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                Label cell = MakeCell();
                cell.Location = new Point(c * CellSize, r * CellSize);
                grid.Controls.Add(cell);
                _cells[r, c] = cell;
            }
        }

        _spaceCell = MakeCell();
        _spaceCell.Text = "SPACE";
        _spaceCell.Location = new Point(0, Rows * CellSize);
        _spaceCell.Size = new Size(CellSize * Cols, CellSize);
        grid.Controls.Add(_spaceCell);

        _repositionTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _repositionTimer.Tick += (_, _) => { if (Visible) Reposition(); };
        _repositionTimer.Start();

        RedrawHighlight();
        Visible = false;
    }

    private Label MakeCell() => new()
    {
        Size = new Size(CellSize - 4, CellSize - 4),
        Margin = new Padding(2),
        BorderStyle = BorderStyle.FixedSingle,
        TextAlign = ContentAlignment.MiddleCenter,
        BackColor = Color.FromArgb(44, 47, 54),
        ForeColor = Color.White,
        Font = new Font("Consolas", 12f, FontStyle.Bold)
    };

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repositionTimer.Stop();
            _repositionTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Reposition()
    {
        Native.RECT? rect = _service.Injector.GetProjectorClientRectScreen();
        if (rect is null)
        {
            return;
        }
        int x = rect.Value.Left + (rect.Value.Width - Width) / 2;
        int y = rect.Value.Bottom - Height - 24;
        Location = new Point(Math.Max(rect.Value.Left, x), Math.Max(rect.Value.Top, y));
    }

    private void UiInvoke(Action action)
    {
        if (IsDisposed)
        {
            return;
        }
        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Toggle()
    {
        bool opening = !_isOpen;
        if (opening && !_service.GetConfig().OskEnabled)
        {

            UiInvoke(() => OpenBlockedByConfig?.Invoke());
            return;
        }
        _isOpen = opening;
        if (opening)
        {
            lock (_navLock)
            {
                _row = 0;
                _col = 0;
                _mode = ShiftMode.Lower;
            }
        }
        UiInvoke(() =>
        {
            if (opening)
            {
                Reposition();
                RedrawHighlight();

                Visible = true;
            }
            else
            {
                Visible = false;
            }
        });
    }

    public void Navigate(int dx, int dy)
    {
        lock (_navLock)
        {
            if (_row == Rows)
            {

                if (dy < 0)
                {
                    _row = Rows - 1;
                    _col = Math.Clamp(_col, 0, Cols - 1);
                }
                UiInvoke(RedrawHighlight);
                return;
            }

            if (dy != 0)
            {
                _row = Math.Clamp(_row + dy, 0, Rows);
                if (_row < Rows)
                {
                    _col = Math.Clamp(_col, 0, RowLength(_row) - 1);
                }
            }
            else if (dx != 0)
            {
                int len = RowLength(_row);
                _col = Math.Clamp(_col + dx, 0, len - 1);
            }
        }
        UiInvoke(RedrawHighlight);
    }

    void IOskHost.Activate()
    {
        char ch;
        bool isSpace;
        lock (_navLock)
        {
            isSpace = _row == Rows;
            ch = isSpace ? ' ' : CurrentRows()[_row][_col];
        }
        if (!isSpace && ch == '\0')
        {
            return;
        }
        if (!_service.Injector.IsProjectorForeground())
        {
            return;
        }
        OskKeyInjector.SendChar(ch);
    }

    public void Backspace()
    {
        if (_service.Injector.IsProjectorForeground())
        {
            OskKeyInjector.TapScancode(OskKeyInjector.VK_BACK);
        }
    }

    public void Submit()
    {
        if (_service.Injector.IsProjectorForeground())
        {
            OskKeyInjector.TapScancode(OskKeyInjector.VK_RETURN);
        }
    }

    public void Tab(bool backward)
    {
        if (!_service.Injector.IsProjectorForeground())
        {
            return;
        }
        if (backward)
        {
            OskKeyInjector.SendScancodeKey(OskKeyInjector.VK_SHIFT, down: true);
            OskKeyInjector.TapScancode(OskKeyInjector.VK_TAB);
            OskKeyInjector.SendScancodeKey(OskKeyInjector.VK_SHIFT, down: false);
        }
        else
        {
            OskKeyInjector.TapScancode(OskKeyInjector.VK_TAB);
        }
    }

    public void CycleShift()
    {
        lock (_navLock)
        {
            _mode = _mode switch
            {
                ShiftMode.Lower => ShiftMode.Upper,
                ShiftMode.Upper => ShiftMode.Symbols,
                _ => ShiftMode.Lower
            };
        }
        UiInvoke(RedrawHighlight);
    }

    private static int RowLength(int row) => Cols;

    private string[] CurrentRows() => _mode switch
    {
        ShiftMode.Upper => RowsUpper,
        ShiftMode.Symbols => RowsSymbols,
        _ => RowsLower
    };

    private void RedrawHighlight()
    {
        string[] rows = CurrentRows();
        int hr, hc;
        lock (_navLock)
        {
            hr = _row;
            hc = _col;
        }

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                Label cell = _cells[r, c];
                char ch = rows[r][c];
                bool blank = ch == '\0';
                cell.Text = blank ? string.Empty : ch.ToString();
                bool hi = r == hr && c == hc && hr < Rows;
                cell.BackColor = blank ? Color.FromArgb(30, 32, 36) : hi ? Color.FromArgb(70, 160, 70) : Color.FromArgb(44, 47, 54);
            }
        }
        _spaceCell.BackColor = hr == Rows ? Color.FromArgb(70, 160, 70) : Color.FromArgb(44, 47, 54);
        _titleLabel.Text = _mode switch
        {
            ShiftMode.Upper => "On-Screen Keyboard — CAPS (North to cycle, R3/Start to close)",
            ShiftMode.Symbols => "On-Screen Keyboard — Symbols (North to cycle, R3/Start to close)",
            _ => "On-Screen Keyboard (North to cycle case, R3/Start to close)"
        };
    }
}
