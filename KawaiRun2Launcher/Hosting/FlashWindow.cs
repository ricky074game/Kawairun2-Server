using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KawaiRun2Launcher.Hosting;

internal static class PlayModeMenu
{
    public static MenuStrip Create(string current, Action onSwitch)
    {
        ToolStripMenuItem html5 = new("HTML5") { Checked = current == PlayMode.Html5 };
        ToolStripMenuItem flash = new("Flash") { Checked = current == PlayMode.Flash };
        (current == PlayMode.Html5 ? flash : html5).Click += (_, _) => onSwitch();

        ToolStripMenuItem version = new("Version");
        version.DropDownItems.Add(html5);
        version.DropDownItems.Add(flash);

        MenuStrip strip = new() { Dock = DockStyle.Top };
        strip.Items.Add(version);
        return strip;
    }
}

internal sealed class FlashForm : Form
{
    private const int GameWidth = 1125;
    private const int GameHeight = 600;
    private const int GwlStyle = -16;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    private static extern bool SetMenu(IntPtr hWnd, IntPtr hMenu);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, uint value);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    private readonly Process _process;
    private readonly Panel _host = new() { Dock = DockStyle.Fill, BackColor = Color.Black };
    private IntPtr _flash;

    public IntPtr FlashHandle => _flash;

    public FlashForm(Process process, string title, Action onSwitch)
    {
        _process = process;
        Text = title;
        Icon = WindowsHost.LoadIcon();
        BackColor = Color.Black;
        StartPosition = FormStartPosition.CenterScreen;

        MenuStrip menu = PlayModeMenu.Create(PlayMode.Flash, () =>
        {
            onSwitch();
            Close();
        });
        Controls.Add(_host);
        Controls.Add(menu);
        MainMenuStrip = menu;
        ClientSize = new Size(GameWidth, GameHeight + menu.Height);

        _host.Resize += (_, _) => FitFlash();
        Activated += (_, _) => FocusFlash();
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(Close);
            }
        };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        for (int attempt = 0; attempt < 100 && _flash == IntPtr.Zero && !_process.HasExited; attempt++)
        {
            IntPtr window = FlashWindowCustomizer.FindMainWindow(_process.Id);
            if (window != IntPtr.Zero)
            {
                SetMenu(window, IntPtr.Zero);
                SetWindowLong(window, GwlStyle, WsChild | WsVisible);
                SetParent(window, _host.Handle);
                _flash = window;
                FitFlash();
                FocusFlash();
                return;
            }

            await Task.Delay(100);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch
        {
        }
    }

    private void FitFlash()
    {
        if (_flash != IntPtr.Zero)
        {
            MoveWindow(_flash, 0, 0, _host.ClientSize.Width, _host.ClientSize.Height, true);
        }
    }

    private void FocusFlash()
    {
        if (_flash != IntPtr.Zero)
        {
            SetFocus(_flash);
        }
    }
}
