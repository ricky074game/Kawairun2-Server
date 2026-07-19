using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Reflection;
using DiscordRPC;

namespace KawaiRun2Launcher;

internal static class Program
{
    private const string DefaultVersion = "1.3.1";
    private const string VersionFileName = "version.txt";
    private const string ConfigFileName = "launcher_config.json";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static async Task<int> Main(string[] args)
    {
        string exeDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        string currentVersionFromApp = DefaultVersion;

        // Use a temp folder tied to our app
        string tempDir = Path.Combine(Path.GetTempPath(), "KawaiRun2Launcher", "Runtime");
        Directory.CreateDirectory(tempDir);

        try
        {
            ExtractResource("launcher_config.json", Path.Combine(tempDir, ConfigFileName));
            ExtractResource("version.txt", Path.Combine(tempDir, VersionFileName));
            ExtractResource("game.swf", Path.Combine(tempDir, "game.swf"));

            currentVersionFromApp = ReadTrimmedFile(Path.Combine(tempDir, VersionFileName)) ?? DefaultVersion;
        }
        catch (Exception ex)
        {
            await UserDialogs.ShowErrorAsync("Initialization Error", $"Failed to extract resources.\n\n{ex.Message}");
            return 1;
        }

        LauncherConfig config = LauncherConfig.Load(Path.Combine(tempDir, ConfigFileName));
        string platformKey = PlatformInfo.GetPackagePlatformKey();

        using var discord = DiscordPresenceService.TryCreate(config.Discord, currentVersionFromApp, platformKey);

        try
        {
            UpdateManifest? manifest = await TryFetchUpdateManifestAsync(config.UpdateManifestUrl);
            if (manifest is not null)
            {
                UpdateDecision decision = UpdateDecision.Create(currentVersionFromApp, platformKey, manifest);
                if (decision.RequiresUpdate)
                {
                    bool shouldInstall = decision.IsCritical ||
                        await UserDialogs.ConfirmAsync(
                            "Update Available",
                            $"Version {decision.TargetVersion} is available for {platformKey}.\n\nInstall now?");

                    if (shouldInstall)
                    {
                        discord?.SetState("Updating game");
                        await InstallUpdateAsync(exeDirectory, decision, manifest);
                        return 0;
                    }
                }
            }

            string flashExecutable = ResolveAndExtractFlash(tempDir);
            string swfPath = Path.Combine(tempDir, "game.swf");
            EnsureFileExists(swfPath, "game.swf");

            discord?.SetState("Launching game");
            using Process gameProcess = StartFlashProcess(flashExecutable, swfPath, tempDir);

            if (OperatingSystem.IsWindows())
            {
                string iconPath = Path.Combine(tempDir, "kawairun2icon.ico");
                ExtractResource("kawairun2icon.ico", iconPath);
                _ = Task.Run(() => FlashWindowCustomizer.TryRenameAndSetIconAsync(gameProcess.Id, config.WindowTitle, iconPath));
            }

            discord?.SetState("Playing KawaiRun 2");
            await gameProcess.WaitForExitAsync();
            return gameProcess.ExitCode;
        }
        catch (LauncherException ex)
        {
            await UserDialogs.ShowErrorAsync(ex.Title, ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            await UserDialogs.ShowErrorAsync("Launcher Error", ex.ToString());
            return 1;
        }
    }

    private static void ExtractResource(string resourceName, string outputPath)
    {
        using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return;
        }
        using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        stream.CopyTo(fs);
    }

    private static string ResolveAndExtractFlash(string extractDir)
    {
        if (OperatingSystem.IsWindows())
        {
            string flashPath = Path.Combine(extractDir, "flash.exe");
            ExtractResource("flash.exe", flashPath);
            EnsureFileExists(flashPath, "flash.exe");
            return flashPath;
        }

        if (OperatingSystem.IsMacOS())
        {
            string macAppZip = Path.Combine(extractDir, "mac_flash.zip");
            string macAppDir = Path.Combine(extractDir, "Flash Player.app");
            string execPath = Path.Combine(macAppDir, "Contents", "MacOS", "Flash Player");


            if (!File.Exists(execPath))
            {
                ExtractResource("mac_flash.zip", macAppZip);
                if (File.Exists(macAppZip))
                {
                    ZipFile.ExtractToDirectory(macAppZip, extractDir, overwriteFiles: true);
                }
            }

            if (File.Exists(execPath))
            {
                RunUnixTool("chmod", "+x", execPath);
                RunUnixTool("xattr", "-dr", "com.apple.quarantine", macAppDir);
                return execPath;
            }
            throw new LauncherException("Missing Flash Projector", "Could not extract or locate macOS Flash projector.");
        }

        if (OperatingSystem.IsLinux())
        {
            string flashPath = Path.Combine(extractDir, "linux_flash");
            ExtractResource("linux_flash", flashPath);
            EnsureFileExists(flashPath, "linux_flash");
            RunUnixTool("chmod", "+x", flashPath);
            return flashPath;
        }

        throw new LauncherException("Unsupported Platform", $"This launcher does not support {RuntimeInformation.OSDescription}.");
    }

    private static void RunUnixTool(string fileName, params string[] arguments)
    {
        try
        {
            ProcessStartInfo info = new()
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string arg in arguments)
            {
                info.ArgumentList.Add(arg);
            }
            Process.Start(info)?.WaitForExit();
        }
        catch
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KawaiRun2Launcher", DefaultVersion));
        return client;
    }

    private static async Task DownloadPackageAsync(string packageUrl, string destinationPath)
    {
        using HttpClient client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KawaiRun2Launcher", DefaultVersion));

        using CancellationTokenSource overallCts = new();
        overallCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            using HttpResponseMessage response = await client.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, overallCts.Token);
            response.EnsureSuccessStatusCode();

            overallCts.CancelAfter(TimeSpan.FromMinutes(30));

            await using Stream input = await response.Content.ReadAsStreamAsync(overallCts.Token);
            await using FileStream output = File.Create(destinationPath);
            byte[] buffer = new byte[1 << 16];
            while (true)
            {
                int read;
                using (CancellationTokenSource readCts = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token))
                {
                    readCts.CancelAfter(TimeSpan.FromSeconds(60));
                    read = await input.ReadAsync(buffer, readCts.Token);
                }

                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), overallCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                "The download timed out because the connection was too slow or was interrupted. " +
                "Please check your internet connection and try again, or download the latest launcher manually from https://kawairun2.puzzlebest.tech.");
        }
    }

    private static async Task<UpdateManifest?> TryFetchUpdateManifestAsync(string manifestUrl)
    {
        try
        {
            string raw = await HttpClient.GetStringAsync(manifestUrl);
            return UpdateManifest.Parse(raw);
        }
        catch
        {
            return null;
        }
    }

    private static async Task InstallUpdateAsync(string exeDirectory, UpdateDecision decision, UpdateManifest manifest)
    {
        if (!manifest.PackageUrls.TryGetValue(decision.PlatformKey, out string? packageUrl) || string.IsNullOrWhiteSpace(packageUrl))
        {
            throw new LauncherException("Update Error", $"No package URL is configured for platform '{decision.PlatformKey}'.");
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "KawaiRun2Launcher", Guid.NewGuid().ToString("N"));
        string packageZip = Path.Combine(tempRoot, "update.zip");
        string extractedDir = Path.Combine(tempRoot, "extracted");
        Directory.CreateDirectory(extractedDir);

        try
        {
            await DownloadPackageAsync(packageUrl, packageZip);

            ZipFile.ExtractToDirectory(packageZip, extractedDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            throw new LauncherException(
                "Update Failed",
                decision.IsCritical
                    ? $"A critical update to {decision.TargetVersion} is required, but the download/install failed.\n\n{ex.Message}\n\nThe game will now close."
                    : $"The update download failed.\n\n{ex.Message}");
        }

        if (OperatingSystem.IsWindows())
        {
            string updaterScript = Path.Combine(tempRoot, "apply-update.cmd");
            string launcherPath = Environment.ProcessPath ?? throw new LauncherException("Update Error", "Unable to locate the current launcher.");
            string launcherName = Path.GetFileName(launcherPath);

            string script = $"""
@echo off
setlocal
timeout /t 2 /nobreak >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "Copy-Item -Path '{EscapeForPowerShell(extractedDir)}\*' -Destination '{EscapeForPowerShell(exeDirectory)}' -Recurse -Force"
start "" "{Path.Combine(exeDirectory, launcherName)}"
endlocal
""";
            File.WriteAllText(updaterScript, script, Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName = updaterScript,
                WorkingDirectory = tempRoot,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Environment.Exit(0);
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            string updaterScript = Path.Combine(tempRoot, "apply-update.sh");
            string launcherPath = Environment.ProcessPath ?? throw new LauncherException("Update Error", "Unable to locate the current launcher.");
            string script = $"""
#!/bin/sh
sleep 2
cp -R "{EscapeForShell(extractedDir)}/." "{EscapeForShell(exeDirectory)}"
chmod +x "{EscapeForShell(launcherPath)}"
open "{EscapeForShell(launcherPath)}" || "{EscapeForShell(launcherPath)}" &
""";
            File.WriteAllText(updaterScript, script, Encoding.UTF8);
            Process.Start("chmod", $"+x \"{updaterScript}\"")?.WaitForExit();
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"\"{updaterScript}\"",
                WorkingDirectory = tempRoot,
                UseShellExecute = false
            });
            Environment.Exit(0);
        }
        else
        {
            throw new LauncherException("Update Error", "Self-update is only implemented for Windows, macOS and Linux.");
        }
    }

    private static Process StartFlashProcess(string flashExecutable, string swfPath, string baseDirectory)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = flashExecutable,
            WorkingDirectory = baseDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(swfPath);

        if (OperatingSystem.IsLinux())
        {
            startInfo.EnvironmentVariables["VDPAU_DRIVER"] = "va_gl";
            startInfo.EnvironmentVariables["LIBVA_DRIVER_NAME"] = "i965";
        }

        try
        {
            string mmsPath = Path.Combine(baseDirectory, "mms.cfg");
            File.WriteAllText(mmsPath, "OverrideGPUValidation=1\nHardwareAccelerationDisabled=0\nEnableLinuxHWVideoDecode=1\n");
        }
        catch { }

        try
        {
            return Process.Start(startInfo)
                   ?? throw new LauncherException("Launcher Error", "The Flash projector did not start.");
        }
        catch (Win32Exception ex)
        {
            throw new LauncherException("Launcher Error", $"Failed to start the Flash projector.\n\n{ex.Message}");
        }
    }

    private static void EnsureFileExists(string path, string displayName)
    {
        if (!File.Exists(path))
        {
            throw new LauncherException("Missing File", $"Required file not found:\n{displayName}\n\nLooked in:\n{path}");
        }
    }

    private static string? ReadTrimmedFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadAllText(path).Trim();
    }

    private static string EscapeForPowerShell(string path) => path.Replace("'", "''");

    private static string EscapeForShell(string path) => path.Replace("\"", "\\\"");
}

internal sealed class LauncherException : Exception
{
    public LauncherException(string title, string message) : base(message)
    {
        Title = title;
    }

    public string Title { get; }
}

internal sealed class LauncherConfig
{
    public string UpdateManifestUrl { get; init; } = "https://kawairun2.puzzlebest.tech/update.txt";
    public string WindowTitle { get; init; } = "KawaiRun 2";
    public DiscordConfig Discord { get; init; } = new();

    public static LauncherConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new LauncherConfig();
        }

        try
        {
            LauncherConfig? config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            return config ?? new LauncherConfig();
        }
        catch
        {
            return new LauncherConfig();
        }
    }
}

internal sealed class DiscordConfig
{
    public bool Enabled { get; init; }
    public string? ApplicationId { get; init; }
    public string Details { get; init; } = "Playing KawaiRun 2";
    public string State { get; init; } = "In launcher";
    public string? LargeImageKey { get; init; }
    public string? LargeImageText { get; init; } = "KawaiRun 2";
}

internal sealed class DiscordPresenceService : IDisposable
{
    private readonly DiscordRpcClient _client;
    private readonly DiscordConfig _config;

    private DiscordPresenceService(DiscordRpcClient client, DiscordConfig config)
    {
        _client = client;
        _config = config;
    }

    public static DiscordPresenceService? TryCreate(DiscordConfig config, string version, string platformKey)
    {
        if (!config.Enabled || string.IsNullOrWhiteSpace(config.ApplicationId))
        {
            return null;
        }

        try
        {
            DiscordRpcClient client = new(config.ApplicationId.Trim());
            if (!client.Initialize())
            {
                return null;
            }

            DiscordPresenceService service = new(client, config);
            service._client.SetPresence(new RichPresence
            {
                Details = config.Details,
                State = $"{config.State} ({platformKey}, v{version})",
                Assets = string.IsNullOrWhiteSpace(config.LargeImageKey)
                    ? null
                    : new Assets
                    {
                        LargeImageKey = config.LargeImageKey,
                        LargeImageText = config.LargeImageText
                    }
            });
            return service;
        }
        catch
        {
            return null;
        }
    }

    public void SetState(string state)
    {
        try
        {
            _client.SetPresence(new RichPresence
            {
                Details = _config.Details,
                State = state,
                Assets = string.IsNullOrWhiteSpace(_config.LargeImageKey)
                    ? null
                    : new Assets
                    {
                        LargeImageKey = _config.LargeImageKey,
                        LargeImageText = _config.LargeImageText
                    }
            });
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        try
        {
            _client.Dispose();
        }
        catch
        {
        }
    }
}

internal sealed class UpdateManifest
{
    public required Version Version { get; init; }
    public bool Critical { get; init; }
    public Dictionary<string, string> PackageUrls { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static UpdateManifest Parse(string raw)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = trimmed[..separatorIndex].Trim();
            string value = trimmed[(separatorIndex + 1)..].Trim();
            values[key] = value;
        }

        if (!values.TryGetValue("version", out string? versionText) || !Version.TryParse(versionText, out Version? version))
        {
            throw new LauncherException("Update Error", "The update manifest is missing a valid version.");
        }

        bool critical = values.TryGetValue("critical", out string? criticalText) &&
                        bool.TryParse(criticalText, out bool parsedCritical) &&
                        parsedCritical;

        Dictionary<string, string> packageUrls = values
            .Where(pair => pair.Key.EndsWith("_url", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                pair => pair.Key[..^4].Replace('_', '-'),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        return new UpdateManifest
        {
            Version = version,
            Critical = critical,
            PackageUrls = packageUrls
        };
    }
}

internal sealed class UpdateDecision
{
    public required bool RequiresUpdate { get; init; }
    public required bool IsCritical { get; init; }
    public required string PlatformKey { get; init; }
    public required Version TargetVersion { get; init; }

    public static UpdateDecision Create(string currentVersion, string platformKey, UpdateManifest manifest)
    {
        Version localVersion = Version.TryParse(currentVersion, out Version? parsed)
            ? parsed
            : new Version(0, 0, 0);

        bool requiresUpdate = manifest.Version > localVersion && manifest.PackageUrls.ContainsKey(platformKey);
        return new UpdateDecision
        {
            RequiresUpdate = requiresUpdate,
            IsCritical = manifest.Critical,
            PlatformKey = platformKey,
            TargetVersion = manifest.Version
        };
    }
}

internal static class PlatformInfo
{
    public static string GetPackagePlatformKey()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.Is64BitOperatingSystem ? "win-x64" : "win-x86";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx-x64";
        }

        // Only an x86_64 Linux projector is bundled, so we always map Linux to linux-x64.
        // (RuntimeInformation.RuntimeIdentifier can report a distro-specific RID that would
        // never match the update manifest keys.)
        if (OperatingSystem.IsLinux())
        {
            return "linux-x64";
        }

        return RuntimeInformation.RuntimeIdentifier;
    }
}

internal static class UserDialogs
{
    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        if (OperatingSystem.IsWindows())
        {
            return NativeMessageBox(IntPtr.Zero, message, title, 0x00000024) == 6;
        }

        if (OperatingSystem.IsMacOS())
        {
            string script = $"display dialog \"{EscapeAppleScript(message)}\" with title \"{EscapeAppleScript(title)}\" buttons {{\"Later\", \"Update\"}} default button \"Update\"";
            ProcessStartInfo info = new()
            {
                FileName = "osascript",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            info.ArgumentList.Add("-e");
            info.ArgumentList.Add(script);
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            // osascript exits 0 for BOTH buttons (only Cancel/Esc exits non-zero), and prints
            // "button returned:Update" to stdout — so we must inspect stdout, not just the code.
            string stdout = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 && stdout.Contains("Update", StringComparison.Ordinal);
        }

        return false;
    }

    public static Task ShowErrorAsync(string title, string message)
    {
        if (OperatingSystem.IsWindows())
        {
            NativeMessageBox(IntPtr.Zero, message, title, 0x00000010);
            return Task.CompletedTask;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start("osascript", $"-e \"display alert \\\"{EscapeAppleScript(title)}\\\" message \\\"{EscapeAppleScript(message)}\\\"\"")?.WaitForExit();
            return Task.CompletedTask;
        }

        Console.Error.WriteLine($"{title}: {message}");
        return Task.CompletedTask;
    }

    private static string EscapeAppleScript(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private static int NativeMessageBox(IntPtr handle, string text, string caption, uint type) => MessageBoxW(handle, text, caption, type);
}

internal static class FlashWindowCustomizer
{
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool SetMenu(IntPtr hWnd, IntPtr hMenu);

    public static async Task TryRenameAndSetIconAsync(int processId, string title, string iconPath)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            IntPtr windowHandle = FindMainWindow(processId);
            if (windowHandle != IntPtr.Zero)
            {
                SetWindowTextW(windowHandle, title);
                SetMenu(windowHandle, IntPtr.Zero);
                if (File.Exists(iconPath))
                {
                    IntPtr hIconSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    IntPtr hIconBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                    if (hIconSmall != IntPtr.Zero) SendMessage(windowHandle, WM_SETICON, (IntPtr)ICON_SMALL, hIconSmall);
                    if (hIconBig != IntPtr.Zero) SendMessage(windowHandle, WM_SETICON, (IntPtr)ICON_BIG, hIconBig);
                }
                return;
            }

            await Task.Delay(200);
        }
    }

    private static IntPtr FindMainWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint ownerPid);
            if (ownerPid == processId && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}