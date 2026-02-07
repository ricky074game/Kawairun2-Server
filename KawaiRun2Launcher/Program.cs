using System.Diagnostics;

static class Program
{
    static void Main()
    {
        string baseDir = AppContext.BaseDirectory;
        string projector = Path.Combine(baseDir, "flash.exe");
        string swf = Path.Combine(baseDir, "game.swf");

        if (!File.Exists(projector) || !File.Exists(swf)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = projector,
            Arguments = $"\"{swf}\"",
            WorkingDirectory = baseDir,
            UseShellExecute = false
        });
    }
}