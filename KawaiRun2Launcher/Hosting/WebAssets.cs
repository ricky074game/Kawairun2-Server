using System.IO.Compression;

namespace KawaiRun2Launcher.Hosting;

internal static class WebAssets
{
    private const string ResourceName = "webgame.zip";
    private const string MarkerFileName = ".kr-complete";

    public static string GetCacheRoot()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "KawaiRun2", "web");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "KawaiRun2", "web");
        }

        string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string dataHome = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".local", "share") : xdg;
        return Path.Combine(dataHome, "KawaiRun2", "web");
    }

    public static string EnsureExtracted(string version)
    {
        string root = GetCacheRoot();
        string versionDir = Path.Combine(root, SanitizeSegment(version));
        Directory.CreateDirectory(root);

        using Stream? resource = typeof(WebAssets).Assembly.GetManifestResourceStream(ResourceName);
        if (resource is null)
        {
            throw new LauncherException("Missing Game Files", "This launcher build does not contain the embedded web game (webgame.zip).");
        }

        string stamp = resource.Length.ToString();
        string marker = Path.Combine(versionDir, MarkerFileName);
        if (File.Exists(marker) && SafeRead(marker) == stamp)
        {
            CleanOldVersions(root, versionDir);
            return versionDir;
        }

        string staging = Path.Combine(root, "_staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using ZipArchive archive = new(resource, ZipArchiveMode.Read);
            string stagingFull = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            byte[] buffer = new byte[1 << 20];

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                string destination = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('\\', '/')));
                if (!destination.StartsWith(stagingFull, StringComparison.Ordinal))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using Stream input = entry.Open();
                using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length);
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                }
            }

            File.WriteAllText(Path.Combine(staging, MarkerFileName), stamp);

            if (Directory.Exists(versionDir))
            {
                try
                {
                    Directory.Delete(versionDir, recursive: true);
                }
                catch
                {
                }
            }

            try
            {
                Directory.Move(staging, versionDir);
            }
            catch (IOException) when (File.Exists(marker) && SafeRead(marker) == stamp)
            {

            }
        }
        finally
        {
            TryDelete(staging);
        }

        if (!File.Exists(marker))
        {
            throw new LauncherException("Missing Game Files", $"Could not extract the game files to:\n{versionDir}");
        }

        CleanOldVersions(root, versionDir);
        return versionDir;
    }

    private static void CleanOldVersions(string root, string keepDir)
    {
        string keepFull = Path.GetFullPath(keepDir).TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            foreach (string dir in Directory.GetDirectories(root))
            {
                if (string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), keepFull, StringComparison.Ordinal))
                {
                    continue;
                }

                TryDelete(dir);
            }
        }
        catch
        {
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string? SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeSegment(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
