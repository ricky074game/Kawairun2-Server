using System.Diagnostics;

namespace KawaiRun2Launcher.Controller;

internal interface IControllerUi
{

    void Run(ControllerService service);

    void RequestOpenConfig();

    void Shutdown();
}

internal sealed class NullControllerUi : IControllerUi
{
    private readonly ManualResetEventSlim _closed = new(false);

    public void Run(ControllerService service) => _closed.Wait();
    public void RequestOpenConfig() {  }
    public void Shutdown() => _closed.Set();
}

internal static class ControllerUiFactory
{
    public static IControllerUi Create() => new KawaiRun2Launcher.Controller.Ui.TrayContext();
}

internal sealed class ControllerService
{
    private const double PollIntervalSeconds = 0.004;
    private const int ProjectorHandleRefreshTicks = 250;

    private readonly object _configLock = new();
    private ControllerRootConfig _config;

    private Process? _gameProcess;
    private Thread? _pollThread;
    private Thread? _uiThread;
    private volatile bool _running;

    public InputInjector Injector { get; }
    public PadManager PadManager { get; }
    public Mapper Mapper { get; }
    public IControllerUi Ui { get; private set; } = new NullControllerUi();

    public event Action? OpenConfigRequested;

    public ControllerService(string tempDir)
    {
        _config = ControllerConfigStore.Load();
        Injector = new InputInjector(() => GetConfig().InjectionMode);
        PadManager = new PadManager(OperatingSystem.IsWindows() ? tempDir : null);

        PadManager.Persist = mutate => Task.Run(() => UpdateConfig(mutate));
        Mapper = new Mapper(Injector, GetConfig);
        Mapper.OpenConfigRequested += () => OpenConfigRequested?.Invoke();
    }

    public ControllerRootConfig GetConfig()
    {
        lock (_configLock)
        {
            return _config;
        }
    }

    public void UpdateConfig(Action<ControllerRootConfig> mutate)
    {
        lock (_configLock)
        {
            ControllerRootConfig next = ControllerConfigStore.Clone(_config);
            mutate(next);
            next.Sanitize();
            ControllerConfigStore.Save(next);
            _config = next;
        }
    }

    public void SuspendInjection(bool suspended) => Mapper.SetSuspended(suspended);

    public void Start(Process gameProcess)
    {
        if (!OperatingSystem.IsWindows() || _running)
        {
            return;
        }

        _gameProcess = gameProcess;
        _running = true;

        IntPtr hwnd = ResolveProjectorHandleWithRetry(gameProcess.Id);
        Injector.SetProjector(gameProcess.Id, hwnd);

        PadManager.Start();

        Native.TimeBeginPeriod(1);

        _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "ControllerPoll" };
        _pollThread.Start();

        Ui = ControllerUiFactory.Create();
        _uiThread = new Thread(() => Ui.Run(this)) { IsBackground = true, Name = "ControllerUi" };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }
        _running = false;

        try
        {
            _pollThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        try
        {

            Mapper.ReleaseAll(1);
            Mapper.ReleaseAll(2);
        }
        catch
        {
        }

        try
        {
            Injector.ForceReleaseAllKnownKeys();
        }
        catch
        {
        }

        try
        {
            PadManager.Stop();
        }
        catch
        {
        }

        try
        {
            Native.TimeEndPeriod(1);
        }
        catch
        {
        }

        try
        {
            Ui.Shutdown();
        }
        catch
        {
        }

        try
        {
            _uiThread?.Join(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
        }
    }

    private static IntPtr ResolveProjectorHandleWithRetry(int processId)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            IntPtr handle = FlashWindowCustomizer.FindMainWindow(processId);
            if (handle != IntPtr.Zero)
            {
                return handle;
            }
            Thread.Sleep(100);
        }
        return IntPtr.Zero;
    }

    private void PollLoop()
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        double lastTickSeconds = stopwatch.Elapsed.TotalSeconds;
        int tickCounter = 0;

        while (_running)
        {
            double tickStart = stopwatch.Elapsed.TotalSeconds;
            double dt = tickStart - lastTickSeconds;
            lastTickSeconds = tickStart;
            if (dt <= 0)
            {
                dt = PollIntervalSeconds;
            }
            else if (dt > 0.25)
            {

                dt = 0.25;
            }

            if (_gameProcess is { HasExited: true })
            {
                _running = false;
                break;
            }

            if (++tickCounter % ProjectorHandleRefreshTicks == 0)
            {
                Injector.RefreshProjectorHandle();
            }

            try
            {
                ControllerRootConfig config = GetConfig();
                PadManager.Update(config);
                PadState pad1 = PadManager.GetPad(1);
                PadState pad2 = PadManager.GetPad(2);
                Mapper.Tick(pad1, pad2, dt);
            }
            catch (Exception ex)
            {
                try
                {
                    Mapper.ReleaseAll(1);
                    Mapper.ReleaseAll(2);
                    Injector.ForceReleaseAllKnownKeys();
                }
                catch
                {
                }
                Debug.WriteLine($"[controller] poll tick failed: {ex}");
                Thread.Sleep(50);
            }

            double elapsedThisTick = stopwatch.Elapsed.TotalSeconds - tickStart;
            double remaining = PollIntervalSeconds - elapsedThisTick;
            if (remaining > 0)
            {
                Thread.Sleep((int)(remaining * 1000));
            }
        }
    }
}
