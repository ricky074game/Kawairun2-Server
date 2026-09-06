using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace KawaiRun2Launcher.Controller.Ui;

internal sealed class HiddenMessageForm : Form
{
    public event Action? HotkeyPressed;

    public HiddenMessageForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Size = new Size(0, 0);
        Opacity = 0;
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            HotkeyPressed?.Invoke();
        }
        base.WndProc(ref m);
    }
}

internal sealed class TrayContext : ApplicationContext, IControllerUi
{
    private const uint VK_G = 0x47;
    private const int HOTKEY_ID = 0xB00 + 1;
    private const uint WM_CLOSE = 0x0010;

    private ControllerService? _service;
    private NotifyIcon? _tray;
    private HiddenMessageForm? _hidden;
    private ConfigForm? _configForm;
    private OskForm? _oskForm;

    public void Run(ControllerService service)
    {
        _service = service;

        _hidden = new HiddenMessageForm();
        _hidden.HotkeyPressed += () => OpenConfigOnUiThread();
        IntPtr handle = _hidden.Handle;
        Native.RegisterHotKey(handle, HOTKEY_ID, Native.MOD_CONTROL | Native.MOD_ALT, VK_G);

        _oskForm = new OskForm(service);
        _ = _oskForm.Handle;
        service.Mapper.OskHost = _oskForm;

        service.OpenConfigRequested += OnOpenConfigRequestedFromService;

        _tray = BuildTrayIcon();
        _oskForm.OpenBlockedByConfig += OnOskOpenBlockedByConfig;

        Application.Run(this);

        try { Native.UnregisterHotKey(handle, HOTKEY_ID); } catch { }
        service.OpenConfigRequested -= OnOpenConfigRequestedFromService;
        _oskForm.OpenBlockedByConfig -= OnOskOpenBlockedByConfig;
        _tray.Visible = false;
        _tray.Dispose();
        _configForm?.Dispose();
        _oskForm?.Dispose();
        _hidden.Dispose();
    }

    private void OnOpenConfigRequestedFromService() => RequestOpenConfig();

    private void OnOskOpenBlockedByConfig()
    {
        _tray?.ShowBalloonTip(3000, "On-screen keyboard disabled",
            "Enable it from Configure Controller → Global.", ToolTipIcon.Info);
    }

    public void RequestOpenConfig()
    {
        HiddenMessageForm? hidden = _hidden;
        if (hidden is null || hidden.IsDisposed)
        {
            return;
        }
        try
        {
            hidden.BeginInvoke(new Action(OpenConfigOnUiThread));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Shutdown()
    {
        HiddenMessageForm? hidden = _hidden;
        if (hidden is null || hidden.IsDisposed)
        {
            return;
        }
        try
        {
            hidden.BeginInvoke(new Action(ExitThread));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OpenConfigOnUiThread()
    {
        if (_service is null)
        {
            return;
        }
        if (_configForm is null || _configForm.IsDisposed)
        {
            _configForm = new ConfigForm(_service);
        }
        if (!_configForm.Visible)
        {
            _configForm.Show();
        }
        _configForm.WindowState = FormWindowState.Normal;
        _configForm.TopMost = true;
        _configForm.BringToFront();
        _configForm.Activate();
        _configForm.TopMost = false;
    }

    private NotifyIcon BuildTrayIcon()
    {
        ContextMenuStrip menu = new();

        ToolStripMenuItem configureItem = new("Configure Controller…");
        configureItem.Click += (_, _) => OpenConfigOnUiThread();

        ToolStripMenuItem enableItem = new("Enable controller support") { CheckOnClick = false };
        enableItem.Click += (_, _) =>
        {
            _service!.UpdateConfig(c => c.Enabled = !c.Enabled);
        };

        ToolStripMenuItem oskItem = new("Open on-screen keyboard");
        oskItem.Click += (_, _) => _oskForm?.Toggle();

        ToolStripMenuItem aboutItem = new("About / x360ce attribution");
        aboutItem.Click += (_, _) => ShowAbout();

        ToolStripMenuItem exitItem = new("Exit game");
        exitItem.Click += (_, _) => ExitGame();

        menu.Items.Add(configureItem);
        menu.Items.Add(enableItem);
        menu.Items.Add(oskItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(aboutItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) => enableItem.Checked = _service!.GetConfig().Enabled;

        NotifyIcon tray = new()
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "KawaiRun 2 — Controller Support",
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => OpenConfigOnUiThread();
        return tray;
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("kawairun2icon.ico");
            if (stream is not null)
            {
                return new Icon(stream);
            }
        }
        catch
        {
        }
        return SystemIcons.Application;
    }

    private void ExitGame()
    {
        IntPtr hwnd = _service?.Injector.ProjectorHwnd ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            Native.PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void ShowAbout()
    {
        string text =
            "KawaiRun 2 Launcher — Controller Support\n\n" +
            "Pad reading: XInput (xinput1_4.dll, falling back to xinput9_1_0.dll) plus a Windows.Gaming.Input " +
            "bridge for non-Xbox controllers (Switch Pro, DualShock/DualSense, generic pads).\n\n" +
            "Controller diagram artwork is vendored from the x360ce project:\n\n" +
            "x360ce - XBOX360 Controller Emulator\n" +
            "http://code.google.com/p/x360ce/\n" +
            "Copyright (C) 2002-2010 Racer_S; Copyright (C) 2010-2013 Robert Krawczyk.\n" +
            "Licensed under the GNU Lesser General Public License v3 (or later). See x360/LICENSE.txt " +
            "and x360/ATTRIBUTION.txt embedded in this launcher for the full license text.\n\n" +
            "This launcher does not install, bundle, or run x360ce itself — see the Help panel in the " +
            "Configure Controller window for guidance on when x360ce might help an unrecognized controller.";
        MessageBox.Show(text, "About Controller Support", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
