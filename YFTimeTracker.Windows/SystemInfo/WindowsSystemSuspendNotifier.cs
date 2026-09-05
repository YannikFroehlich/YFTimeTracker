using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.SystemInfo;

/// <summary>
/// Leitet die Energieereignisse von Windows weiter. Auf Geräten mit modernem Standby (S0) meldet
/// Windows den Wechsel nicht zuverlässig; die Lückenerkennung im Tracking bleibt deshalb als
/// Rückfallebene bestehen.
/// </summary>
public sealed class WindowsSystemSuspendNotifier : ISystemSuspendNotifier, IDisposable
{
    private readonly ILogger<WindowsSystemSuspendNotifier> logger;
    private bool disposed;

    public WindowsSystemSuspendNotifier(ILogger<WindowsSystemSuspendNotifier> logger)
    {
        this.logger = logger;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        logger.LogInformation("Listening for system power events.");
    }

    public event EventHandler? Suspending;

    public event EventHandler? Resumed;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                logger.LogInformation("The system is suspending.");
                Suspending?.Invoke(this, EventArgs.Empty);
                break;
            case PowerModes.Resume:
                logger.LogInformation("The system resumed from suspension.");
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
