using System.Diagnostics;
using System.Reflection;

static class Program
{
    static void Main()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "KawaiRun2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string projector = ExtractResourceToFile("flash.exe", tempDir);
            string swf = ExtractResourceToFile("game.swf", tempDir);

            var psi = new ProcessStartInfo
            {
                FileName = projector,
                Arguments = $"\"{swf}\"",
                WorkingDirectory = tempDir,
                UseShellExecute = false
            };

            using (var proc = Process.Start(psi))
            {
                proc.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Launcher error: {ex.Message}");
        }
        finally
        {
            // Try to remove temp dir (retry because file locks may persist briefly)
            for (int i = 0; i < 8; i++)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                    break;
                }
                catch
                {
                    Thread.Sleep(300);
                }
            }
        }
    }

    static string ExtractResourceToFile(string fileName, string destDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        string resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new FileNotFoundException("Embedded resource not found: " + fileName);

        string destPath = Path.Combine(destDir, fileName);
        using (var rs = asm.GetManifestResourceStream(resourceName))
        {
            if (rs == null) throw new InvalidOperationException("Failed to open resource stream: " + resourceName);
            using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                rs.CopyTo(fs);
        }

        return destPath;
    }
}