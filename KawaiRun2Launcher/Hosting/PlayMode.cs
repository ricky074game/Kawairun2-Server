using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace KawaiRun2Launcher.Hosting;

internal static class PlayMode
{
    public const string Html5 = "html5";
    public const string Flash = "flash";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "KawaiRun2Launcher",
        "launcher_settings.json");

    public static string Load()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return document.RootElement.GetProperty("mode").GetString() == Flash ? Flash : Html5;
        }
        catch
        {
            return Html5;
        }
    }

    public static void Save(string mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["mode"] = mode }));
        }
        catch
        {
        }
    }
}

internal sealed class FlashSwitchListener : IDisposable
{
    private const int Port = 47810;
    private static readonly byte[] Reply = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private readonly TcpListener _listener = new(IPAddress.Loopback, Port);
    private readonly Action _onSwitch;

    public FlashSwitchListener(Action onSwitch)
    {
        _onSwitch = onSwitch;
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }
        catch (SocketException)
        {
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync();
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[1024];
                int read = await stream.ReadAsync(buffer);
                string request = Encoding.ASCII.GetString(buffer, 0, read);
                await stream.WriteAsync(Reply);
                if (request.Contains("/__kr/switch", StringComparison.Ordinal))
                {
                    _onSwitch();
                }
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or SocketException or IOException)
        {
        }
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}
