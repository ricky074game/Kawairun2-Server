using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KawaiRun2Launcher.Controller;

internal static class JobObject
{
    private static readonly object Lock = new();
    private static IntPtr _handle = IntPtr.Zero;
    private static bool _createFailed;

    public static void AssignToKillOnCloseJob(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        IntPtr job = GetOrCreateJob();
        if (job == IntPtr.Zero)
        {
            return;
        }

        Native.AssignProcessToJobObject(job, process.Handle);
    }

    private static IntPtr GetOrCreateJob()
    {
        lock (Lock)
        {
            if (_handle != IntPtr.Zero || _createFailed)
            {
                return _handle;
            }

            IntPtr job = Native.CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                _createFailed = true;
                return IntPtr.Zero;
            }

            Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new()
            {
                BasicLimitInformation = new Native.JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };
            uint size = (uint)Marshal.SizeOf<Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            if (!Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, ref info, size))
            {
                _createFailed = true;
                return IntPtr.Zero;
            }

            _handle = job;
            return _handle;
        }
    }
}

internal sealed class BridgeSource : IDisposable
{
    private const string ResourceName = "gamepad-bridge.ps1";
    private const int MaxRestarts = 5;
    private static readonly TimeSpan HealthyRunThreshold = TimeSpan.FromSeconds(120);

    private readonly string _scriptPath;
    private readonly object _lock = new();
    private readonly Dictionary<int, RawBridgeFrame> _frames = new();

    private Process? _process;
    private Thread? _readerThread;
    private volatile bool _stopping;
    private int _restartCount;
    private DateTime _lastSpawnUtc;

    public BridgeSource(string tempDir)
    {
        _scriptPath = Path.Combine(tempDir, ResourceName);
    }

    public bool BridgeUnavailable { get; private set; }

    public void Start()
    {
        try
        {
            ExtractScript();
            SpawnProcess();
        }
        catch
        {

        }
    }

    private void ExtractScript()
    {
        using Stream? stream = typeof(BridgeSource).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return;
        }
        using FileStream fs = new(_scriptPath, FileMode.Create, FileAccess.Write);
        stream.CopyTo(fs);
    }

    private void SpawnProcess()
    {
        if (_stopping || !File.Exists(_scriptPath))
        {
            return;
        }

        ProcessStartInfo psi = new("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{_scriptPath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        try
        {
            _process = Process.Start(psi);
        }
        catch
        {
            _process = null;
            return;
        }

        if (_process is null)
        {
            return;
        }

        _lastSpawnUtc = DateTime.UtcNow;

        try
        {
            JobObject.AssignToKillOnCloseJob(_process);
        }
        catch
        {

        }

        _readerThread = new Thread(ReaderLoop) { IsBackground = true, Name = "GamepadBridgeReader" };
        _readerThread.Start();
    }

    private void ReaderLoop()
    {
        Process? proc = _process;
        if (proc is null)
        {
            return;
        }

        try
        {
            string? line;
            while (!_stopping && (line = proc.StandardOutput.ReadLine()) != null)
            {
                ParseLine(line);
            }
        }
        catch
        {

        }

        if (!_stopping)
        {
            HandleUnexpectedExit();
        }
    }

    private void HandleUnexpectedExit()
    {
        lock (_lock)
        {
            _frames.Clear();
        }

        if (DateTime.UtcNow - _lastSpawnUtc >= HealthyRunThreshold)
        {
            _restartCount = 0;
        }

        if (_restartCount >= MaxRestarts)
        {
            BridgeUnavailable = true;
            return;
        }

        _restartCount++;
        int backoffMs = Math.Min(1000 * _restartCount, 5000);
        Thread.Sleep(backoffMs);
        SpawnProcess();
    }

    private void ParseLine(string line)
    {
        line = line.Trim();
        if (line.Length == 0)
        {
            return;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("none", out _))
            {
                lock (_lock)
                {
                    _frames.Clear();
                }
                return;
            }

            int index = root.TryGetProperty("i", out JsonElement ie) ? ie.GetInt32() : 0;
            string id = root.TryGetProperty("id", out JsonElement ide) ? ide.GetString() ?? string.Empty : string.Empty;
            string vid = root.TryGetProperty("vid", out JsonElement ve) ? ve.GetString() ?? string.Empty : string.Empty;
            string pid = root.TryGetProperty("pid", out JsonElement pe) ? pe.GetString() ?? string.Empty : string.Empty;

            float[] axes = Array.Empty<float>();
            if (root.TryGetProperty("a", out JsonElement ae) && ae.ValueKind == JsonValueKind.Array)
            {
                axes = ae.EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
            }

            bool[] buttons = Array.Empty<bool>();
            if (root.TryGetProperty("b", out JsonElement be) && be.ValueKind == JsonValueKind.Array)
            {
                buttons = be.EnumerateArray().Select(x => x.GetInt32() != 0).ToArray();
            }

            RawBridgeFrame frame = new()
            {
                Index = index,
                Id = id,
                Vid = vid,
                Pid = pid,
                Axes = axes,
                Buttons = buttons,
                LastSeenUtc = DateTime.UtcNow
            };

            lock (_lock)
            {
                _frames[index] = frame;
            }
        }
        catch
        {

        }
    }

    public List<RawBridgeFrame> GetFrames()
    {
        lock (_lock)
        {
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-3);
            List<int> stale = _frames.Where(kv => kv.Value.LastSeenUtc < cutoff).Select(kv => kv.Key).ToList();
            foreach (int key in stale)
            {
                _frames.Remove(key);
            }
            return _frames.Values.ToList();
        }
    }

    public void Dispose()
    {
        _stopping = true;
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
        try
        {
            _process?.Dispose();
        }
        catch
        {
        }
    }
}
