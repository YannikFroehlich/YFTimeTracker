using System.Diagnostics;

namespace YFTimeTracker.App.Services;

public sealed class WindowsExplorerService : IExplorerService
{
    public void RevealFile(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }
}
