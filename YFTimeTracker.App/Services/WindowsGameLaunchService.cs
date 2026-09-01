using System.Diagnostics;

namespace YFTimeTracker.App.Services;

public sealed class WindowsGameLaunchService : IGameLaunchService
{
    public void Launch(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            throw new FileNotFoundException("Die Programmdatei wurde nicht gefunden.", executablePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            UseShellExecute = true
        });
    }
}
