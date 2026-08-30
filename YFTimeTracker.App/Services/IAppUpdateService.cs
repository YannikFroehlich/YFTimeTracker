namespace YFTimeTracker.App.Services;

public interface IAppUpdateService
{
    event EventHandler<AppUpdateState>? StateChanged;

    AppUpdateState State { get; }

    Task<AppUpdateState> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task<AppUpdateState> DownloadUpdateAsync(IProgress<int>? progress, CancellationToken cancellationToken);

    void ScheduleInstallAndRestart();
}
