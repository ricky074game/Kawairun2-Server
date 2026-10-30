using System.Runtime.InteropServices;

namespace KawaiRun2Launcher.Hosting;

internal sealed class HostLaunch
{
    public required string Url { get; init; }
    public required string Title { get; init; }
    public bool DevTools { get; init; }

    public Action<IntPtr>? OnWindowReady { get; init; }
    public Action? OnWindowClosed { get; init; }
    public Action<Action>? OnCloseAvailable { get; init; }
    public Action? OnSwitchMode { get; init; }
}

internal static class ShutdownSignal
{

    public static void Wait()
    {
        using ManualResetEventSlim stop = new(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        List<PosixSignalRegistration> registrations = new();
        foreach (PosixSignal signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP })
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, ctx =>
                {
                    ctx.Cancel = true;
                    stop.Set();
                }));
            }
            catch
            {
            }
        }

        stop.Wait();
        foreach (PosixSignalRegistration registration in registrations)
        {
            registration.Dispose();
        }
    }
}
