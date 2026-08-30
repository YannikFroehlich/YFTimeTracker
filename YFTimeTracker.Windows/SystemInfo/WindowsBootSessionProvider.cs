using System.Globalization;
using System.Runtime.InteropServices;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.SystemInfo;

public sealed class WindowsBootSessionProvider(IClock clock) : IBootSessionProvider
{
    public string GetCurrentBootSessionId()
    {
        var uptime = TimeSpan.FromMilliseconds(GetTickCount64());
        var bootUtc = clock.UtcNow - uptime;
        return bootUtc.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
    }

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();
}
