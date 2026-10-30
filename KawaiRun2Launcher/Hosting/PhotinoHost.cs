using System.Runtime.InteropServices;
using Photino.NET;

namespace KawaiRun2Launcher.Hosting;

internal static class PhotinoHost
{
    private const int BaseWidth = 1125;
    private const int BaseHeight = 600;

    public static int Run(HostLaunch launch, string? iconPath)
    {
        try
        {
            PhotinoWindow window = new PhotinoWindow()
                .SetTitle(launch.Title)
                .SetUseOsDefaultLocation(true)
                .SetSize(BaseWidth, BaseHeight)
                .SetResizable(true)
                .SetContextMenuEnabled(launch.DevTools)
                .SetDevToolsEnabled(launch.DevTools);

            if (iconPath is not null && File.Exists(iconPath))
            {
                try { window.SetIconFile(iconPath); } catch { }
            }

            window.Load(launch.Url);
            launch.OnCloseAvailable?.Invoke(() => window.Invoke(window.Close));
            launch.OnWindowReady?.Invoke(IntPtr.Zero);
            window.WaitForClose();
            launch.OnWindowClosed?.Invoke();
            return 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine("Could not start the embedded window (" + ex.Message + "). " +
                                    (OperatingSystem.IsLinux() ? "On Linux this usually means libwebkit2gtk is not installed. " : "") +
                                    "Falling back to your default web browser.");
            return RunBrowserFallback(launch);
        }
    }

    public static int RunBrowserFallback(HostLaunch launch)
    {
        Console.WriteLine("Kawairun2 is running at " + launch.Url);
        Console.WriteLine("Keep this process running while you play. Press Ctrl+C to quit.");
        ExternalLinks.TryOpen(launch.Url);
        launch.OnWindowReady?.Invoke(IntPtr.Zero);
        ShutdownSignal.Wait();
        launch.OnWindowClosed?.Invoke();
        return 0;
    }

}
