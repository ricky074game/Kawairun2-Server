using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace KawaiRun2Launcher.Hosting;

internal static class WindowsHost
{
    public const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    public static int Run(HostLaunch launch)
    {

        try { SetThreadDpiAwarenessContext(PerMonitorAwareV2); } catch { }

        using GameForm form = new(launch);
        launch.OnCloseAvailable?.Invoke(() => form.BeginInvoke(form.Close));
        Application.Run(form);

        if (form.Failure is not null)
        {
            MessageBox.Show(
                "The embedded browser could not be started:\n\n" + form.Failure.Message +
                "\n\nThe game will open in your default web browser instead.",
                "Kawairun2",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return RunBrowserFallback(launch);
        }

        return 0;
    }

    public static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch
        {
            return false;
        }
    }

    public static int RunBrowserFallback(HostLaunch launch)
    {
        ExternalLinks.TryOpen(launch.Url);

        using Form form = new()
        {
            Text = launch.Title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(380, 130),
            Icon = LoadIcon()
        };
        Label label = new()
        {
            Text = launch.Title + " is running in your browser.\nKeep this window open while you play.\n" + launch.Url,
            Location = new Point(12, 12),
            Size = new Size(356, 60)
        };
        Button open = new() { Text = "Open game", Location = new Point(12, 88), Size = new Size(110, 28) };
        open.Click += (_, _) => ExternalLinks.TryOpen(launch.Url);
        Button quit = new() { Text = "Quit", Location = new Point(258, 88), Size = new Size(110, 28) };
        quit.Click += (_, _) => form.Close();
        form.Controls.AddRange(new Control[] { label, open, quit });
        form.FormClosed += (_, _) => launch.OnWindowClosed?.Invoke();
        Application.Run(form);
        return 0;
    }

    internal static Icon? LoadIcon()
    {
        try
        {
            using Stream? stream = typeof(WindowsHost).Assembly.GetManifestResourceStream("kawairun2icon.ico");
            return stream is null ? null : new Icon(stream);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class GameForm : Form
{
    private const int BaseWidth = 1125;
    private const int BaseHeight = 600;
    private const int VkF11 = 0x7A;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    private readonly HostLaunch _launch;
    private CoreWebView2Controller? _controller;
    private bool _fullscreen;
    private Rectangle _restoreBounds;
    private FormWindowState _restoreState;
    private IntPtr _handle;
    private readonly MenuStrip _menu;

    public Exception? Failure { get; private set; }

    public GameForm(HostLaunch launch)
    {
        _launch = launch;
        Text = launch.Title;
        Icon = WindowsHost.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        _menu = PlayModeMenu.Create(PlayMode.Html5, () =>
        {
            _launch.OnSwitchMode?.Invoke();
            Close();
        });
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        ClientSize = new Size(BaseWidth, BaseHeight + _menu.Height);

        Activated += (_, _) => FocusWebView();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _handle = Handle;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_controller is null)
        {
            return;
        }

        bool minimized = WindowState == FormWindowState.Minimized;
        _controller.IsVisible = !minimized;
        if (!minimized)
        {
            _controller.Bounds = ViewBounds();
        }
    }

    private Rectangle ViewBounds()
    {
        int top = _menu.Visible ? _menu.Height : 0;
        return new Rectangle(0, top, ClientSize.Width, Math.Max(1, ClientSize.Height - top));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        double scale = 1.0;
        try { scale = GetDpiForWindow(Handle) / 96.0; } catch { }
        if (scale < 0.5) scale = 1.0;

        Rectangle area = Screen.FromControl(this).WorkingArea;
        int width = (int)(BaseWidth * scale);
        int height = (int)(BaseHeight * scale);
        int maxWidth = (int)(area.Width * 0.95) - (Width - ClientSize.Width);
        int maxHeight = (int)(area.Height * 0.95) - (Height - ClientSize.Height);
        double fit = Math.Min(1.0, Math.Min((double)maxWidth / width, (double)maxHeight / height));
        if (fit < 1.0)
        {
            width = (int)(width * fit);
            height = (int)(height * fit);
        }

        ClientSize = new Size(width, height + _menu.Height);
        CenterToScreen();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _launch.OnWindowReady?.Invoke(_handle);
        await InitWebViewAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _launch.OnWindowClosed?.Invoke();
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KawaiRun2", "WebView2");
            Directory.CreateDirectory(userData);

            CoreWebView2EnvironmentOptions options = new()
            {

                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --force_high_performance_gpu --ignore-gpu-blocklist --enable-gpu-rasterization --enable-zero-copy --use-webgpu-power-preference=default-high-performance --enable-unsafe-webgpu"
            };
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userData, options);
            CoreWebView2Controller controller = await env.CreateCoreWebView2ControllerAsync(_handle);
            _controller = controller;
            controller.DefaultBackgroundColor = Color.Black;
            controller.Bounds = ViewBounds();
            controller.IsVisible = WindowState != FormWindowState.Minimized;

            CoreWebView2 core = controller.CoreWebView2;
            bool dev = _launch.DevTools;
            core.Settings.AreDevToolsEnabled = dev;
            core.Settings.AreDefaultContextMenusEnabled = dev;
            core.Settings.AreBrowserAcceleratorKeysEnabled = dev;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;

            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                OpenExternal(args.Uri);
            };
            core.NavigationStarting += (_, args) =>
            {
                if (IsExternal(args.Uri))
                {
                    args.Cancel = true;
                    OpenExternal(args.Uri);
                }
            };
            core.ContainsFullScreenElementChanged += (_, _) => SetFullscreen(core.ContainsFullScreenElement);
            core.ProcessFailed += (_, args) =>
            {
                if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                {
                    BeginInvoke(() => core.Reload());
                }
            };

            controller.AcceleratorKeyPressed += (_, args) =>
            {
                if (args.VirtualKey == VkF11 &&
                    args.KeyEventKind is CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown)
                {
                    args.Handled = true;
                    if (args.PhysicalKeyStatus.WasKeyDown == 0)
                    {
                        BeginInvoke(() => SetFullscreen(!_fullscreen));
                    }
                }
            };

            core.NavigationCompleted += (_, _) => FocusWebView();
            core.Navigate(_launch.Url);
            FocusWebView();
        }
        catch (Exception ex)
        {
            Failure = ex;
            Close();
        }
    }

    private void FocusWebView()
    {
        try
        {
            _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }
        catch
        {
        }
    }

    private bool IsExternal(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !Uri.TryCreate(_launch.Url, UriKind.Absolute, out Uri? local))
        {
            return false;
        }

        bool web = parsed.Scheme is "http" or "https";
        return web && !(parsed.Host == local.Host && parsed.Port == local.Port);
    }

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.Scheme is "http" or "https")
        {
            ExternalLinks.TryOpen(parsed.AbsoluteUri);
        }
    }

    private void SetFullscreen(bool on)
    {
        if (on == _fullscreen)
        {
            return;
        }

        if (on)
        {
            _restoreState = WindowState;
            _restoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Rectangle screen = Screen.FromControl(this).Bounds;
            WindowState = FormWindowState.Normal;
            _menu.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screen;
            _fullscreen = true;
        }
        else
        {
            _fullscreen = false;
            _menu.Visible = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _restoreState;
            if (_restoreState == FormWindowState.Normal)
            {
                Bounds = _restoreBounds;
            }
        }

        FocusWebView();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _controller?.Close(); } catch { }
        }
        base.Dispose(disposing);
    }
}
