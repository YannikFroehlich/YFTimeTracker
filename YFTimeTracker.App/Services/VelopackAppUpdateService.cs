using System.Reflection;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace YFTimeTracker.App.Services;

public sealed class VelopackAppUpdateService : IAppUpdateService, IDisposable
{
    private const string RepositoryUrl = "https://github.com/YannikFroehlich/YFTimeTracker";
    private readonly ILogger<VelopackAppUpdateService> logger;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly object stateLock = new();
    private readonly UpdateManager? updateManager;
    private UpdateInfo? availableUpdate;
    private VelopackAsset? downloadedUpdate;
    private AppUpdateState state;

    public VelopackAppUpdateService(ILogger<VelopackAppUpdateService> logger)
    {
        this.logger = logger;
        var fallbackVersion = GetAssemblyVersion();

        try
        {
            var source = new GithubSource(RepositoryUrl, accessToken: null, prerelease: false);
            updateManager = new UpdateManager(source);
            var currentVersion = updateManager.CurrentVersion?.ToNormalizedString() ?? fallbackVersion;

            if (!updateManager.IsInstalled)
            {
                state = new AppUpdateState(
                    AppUpdateStage.Unavailable,
                    currentVersion,
                    "Automatische Updates sind in der installierten Setup-/MSI-Version verfügbar.");
                return;
            }

            if (updateManager.UpdatePendingRestart is { } pendingUpdate)
            {
                downloadedUpdate = pendingUpdate;
                state = new AppUpdateState(
                    AppUpdateStage.ReadyToInstall,
                    currentVersion,
                    $"Version {pendingUpdate.Version.ToNormalizedString()} ist bereit zur Installation.",
                    pendingUpdate.Version.ToNormalizedString(),
                    pendingUpdate.Size,
                    100,
                    pendingUpdate.NotesMarkdown);
                return;
            }

            state = new AppUpdateState(
                AppUpdateStage.Idle,
                currentVersion,
                "Beim App-Start wird automatisch nach neuen Versionen gesucht.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Velopack update manager could not be initialized.");
            state = new AppUpdateState(
                AppUpdateStage.Unavailable,
                fallbackVersion,
                "Die Update-Funktion ist in dieser Ausgabe nicht verfügbar.");
        }
    }

    public event EventHandler<AppUpdateState>? StateChanged;

    public AppUpdateState State
    {
        get
        {
            lock (stateLock)
            {
                return state;
            }
        }
    }

    public async Task<AppUpdateState> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (updateManager is null || !updateManager.IsInstalled)
        {
            return State;
        }

        await operationLock.WaitAsync(cancellationToken);
        try
        {
            SetState(State with
            {
                Stage = AppUpdateStage.Checking,
                Message = "Neue Version wird gesucht …",
                DownloadProgress = 0
            });

            availableUpdate = await updateManager.CheckForUpdatesAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (availableUpdate is null)
            {
                downloadedUpdate = null;
                SetState(State with
                {
                    Stage = AppUpdateStage.UpToDate,
                    Message = $"YFTimeTracker {State.CurrentVersion} ist aktuell.",
                    AvailableVersion = null,
                    DownloadSize = 0,
                    DownloadProgress = 0,
                    ReleaseNotes = null
                });
                return State;
            }

            var target = availableUpdate.TargetFullRelease;
            SetState(State with
            {
                Stage = AppUpdateStage.Available,
                Message = $"Version {target.Version.ToNormalizedString()} ist verfügbar.",
                AvailableVersion = target.Version.ToNormalizedString(),
                DownloadSize = target.Size,
                DownloadProgress = 0,
                ReleaseNotes = target.NotesMarkdown
            });
        }
        catch (OperationCanceledException)
        {
            SetState(State with
            {
                Stage = AppUpdateStage.Idle,
                Message = "Update-Prüfung wurde abgebrochen."
            });
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Checking for application updates failed.");
            SetState(State with
            {
                Stage = AppUpdateStage.Failed,
                Message = "Update-Prüfung fehlgeschlagen. Bitte Internetverbindung prüfen."
            });
        }
        finally
        {
            operationLock.Release();
        }

        return State;
    }

    public async Task<AppUpdateState> DownloadUpdateAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (updateManager is null || !updateManager.IsInstalled)
        {
            return State;
        }

        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var update = availableUpdate
                ?? throw new InvalidOperationException("Es wurde noch kein verfügbares Update gefunden.");

            SetDownloadProgress(0, progress);
            await updateManager.DownloadUpdatesAsync(
                update,
                value => SetDownloadProgress(value, progress),
                cancellationToken);

            downloadedUpdate = updateManager.UpdatePendingRestart ?? update.TargetFullRelease;
            SetState(State with
            {
                Stage = AppUpdateStage.ReadyToInstall,
                Message = $"Version {downloadedUpdate.Version.ToNormalizedString()} ist bereit zur Installation.",
                AvailableVersion = downloadedUpdate.Version.ToNormalizedString(),
                DownloadSize = downloadedUpdate.Size,
                DownloadProgress = 100,
                ReleaseNotes = downloadedUpdate.NotesMarkdown
            });
            progress?.Report(100);
        }
        catch (OperationCanceledException)
        {
            SetState(State with
            {
                Stage = AppUpdateStage.Available,
                Message = "Update-Download wurde abgebrochen.",
                DownloadProgress = 0
            });
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Downloading application update failed.");
            SetState(State with
            {
                Stage = AppUpdateStage.Failed,
                Message = "Update konnte nicht heruntergeladen werden. Bitte später erneut versuchen.",
                DownloadProgress = 0
            });
            throw;
        }
        finally
        {
            operationLock.Release();
        }

        return State;
    }

    public void ScheduleInstallAndRestart()
    {
        if (updateManager is null || downloadedUpdate is null)
        {
            throw new InvalidOperationException("Es ist kein heruntergeladenes Update zur Installation bereit.");
        }

        SetState(State with
        {
            Stage = AppUpdateStage.Applying,
            Message = "Update wird beim Neustart installiert."
        });

        updateManager.WaitExitThenApplyUpdates(
            downloadedUpdate,
            silent: false,
            restart: true,
            restartArgs: Array.Empty<string>());
    }

    public void Dispose()
    {
        operationLock.Dispose();
    }

    private void SetDownloadProgress(int value, IProgress<int>? progress)
    {
        var normalized = Math.Clamp(value, 0, 100);
        SetState(State with
        {
            Stage = AppUpdateStage.Downloading,
            Message = $"Update wird heruntergeladen · {normalized} %",
            DownloadProgress = normalized
        });
        progress?.Report(normalized);
    }

    private void SetState(AppUpdateState newState)
    {
        lock (stateLock)
        {
            state = newState;
        }

        StateChanged?.Invoke(this, newState);
    }

    private static string GetAssemblyVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "unbekannt" : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
