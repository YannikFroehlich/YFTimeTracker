using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

public sealed class GameTrackingService(
    IGameRepository games,
    IGameSessionRepository sessions,
    IProcessSnapshotProvider processSnapshotProvider,
    IGameInstallationProvider installationProvider,
    IBootSessionProvider bootSessionProvider,
    ISettingsStore settings,
    IClock clock,
    ILogger<GameTrackingService> logger) : IGameTrackingService
{
    private static readonly TimeSpan DefaultScanInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LauncherRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly string[] ExcludedExecutableParts =
    [
        "unins", "uninstall", "crash", "reporter", "launcher", "updater", "update.exe",
        "setup", "easyanticheat", "eac", "beservice", "battleye", "cefprocess"
    ];

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, DiscoveryCandidate> discoveryCandidates = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? runCancellation;
    private Task? runTask;
    private LauncherDiscoveryResult launcherCatalog = LauncherDiscoveryResult.Empty;
    private DateTimeOffset launcherCatalogUpdatedAtUtc = DateTimeOffset.MinValue;
    private long scanNumber;
    private bool isPaused;

    public TrackingState State { get; private set; } = TrackingState.Stopped;

    public event EventHandler<TrackingState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (runTask is not null)
            {
                return;
            }

            isPaused = !await settings.GetBoolAsync(AppSettingKeys.TrackingEnabled, true, cancellationToken);
            runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await RecoverOpenSessionsCoreAsync(cancellationToken);
            runTask = RunAsync(runCancellation.Token);
            await PublishStateAsync(true, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? cancellationToStop;
        Task? taskToStop;

        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToStop = runCancellation;
            taskToStop = runTask;
            runCancellation = null;
            runTask = null;
        }
        finally
        {
            gate.Release();
        }

        if (cancellationToStop is not null)
        {
            await cancellationToStop.CancelAsync();
            cancellationToStop.Dispose();
        }

        if (taskToStop is not null)
        {
            try
            {
                await taskToStop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            await CloseAllOpenSessionsAsync(clock.UtcNow, cancellationToken);
            discoveryCandidates.Clear();
            await PublishStateAsync(false, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task PauseAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            isPaused = true;
            discoveryCandidates.Clear();
            await settings.SetAsync(AppSettingKeys.TrackingEnabled, bool.FalseString, cancellationToken);
            await CloseAllOpenSessionsAsync(clock.UtcNow, cancellationToken);
            await PublishStateAsync(runTask is not null, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            isPaused = false;
            await settings.SetAsync(AppSettingKeys.TrackingEnabled, bool.TrueString, cancellationToken);
            await ScanOnceCoreAsync(cancellationToken);
            await PublishStateAsync(runTask is not null, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RecoverOpenSessionsAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await RecoverOpenSessionsCoreAsync(cancellationToken);
            await PublishStateAsync(runTask is not null, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!isPaused)
            {
                await ScanOnceCoreAsync(cancellationToken);
            }

            await PublishStateAsync(runTask is not null, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        gate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var intervalSeconds = await settings.GetIntAsync(
            AppSettingKeys.TrackingIntervalSeconds,
            Convert.ToInt32(DefaultScanInterval.TotalSeconds),
            cancellationToken);
        var interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 1, 60));

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await ScanOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Tracking scan failed.");
            }
        }
    }

    private async Task RecoverOpenSessionsCoreAsync(CancellationToken cancellationToken)
    {
        var openSessions = await sessions.GetOpenSessionsAsync(cancellationToken);
        if (openSessions.Count == 0)
        {
            if (!isPaused)
            {
                await ScanOnceCoreAsync(cancellationToken);
            }

            return;
        }

        var currentBootId = bootSessionProvider.GetCurrentBootSessionId();
        var runningProcesses = await processSnapshotProvider.GetRunningProcessesAsync(cancellationToken);
        var runningPathKeys = runningProcesses.Select(process => process.ExecutablePathKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var session in openSessions)
        {
            var game = session.Game ?? await games.GetByIdAsync(session.GameId, cancellationToken);
            var stillRunning = game is not null
                && game.Executables.Any(executable => runningPathKeys.Contains(executable.ExecutablePathKey))
                && string.Equals(session.BootSessionId, currentBootId, StringComparison.Ordinal);

            if (!stillRunning)
            {
                session.Close(session.LastSeenAtUtc);
                await sessions.UpdateAsync(session, cancellationToken);
            }
        }

        if (!isPaused)
        {
            await ScanOnceCoreAsync(cancellationToken);
        }
    }

    private async Task ScanOnceCoreAsync(CancellationToken cancellationToken)
    {
        scanNumber++;
        var now = clock.UtcNow;
        var runningProcesses = await processSnapshotProvider.GetRunningProcessesAsync(cancellationToken);
        var runningPathKeys = runningProcesses.Select(process => process.ExecutablePathKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sessionStartOverrides = new Dictionary<long, DateTimeOffset>();

        if (await settings.GetBoolAsync(AppSettingKeys.LauncherDiscoveryEnabled, true, cancellationToken))
        {
            await RefreshLauncherCatalogIfNeededAsync(now, cancellationToken);
            await DiscoverRunningLauncherGamesAsync(runningProcesses, now, sessionStartOverrides, cancellationToken);
        }
        else
        {
            discoveryCandidates.Clear();
        }

        var knownGames = await games.GetAllAsync(cancellationToken);
        var currentBootId = bootSessionProvider.GetCurrentBootSessionId();
        var openSessions = await sessions.GetOpenSessionsAsync(cancellationToken);
        var openByGameId = openSessions.ToDictionary(session => session.GameId);
        var heartbeatSeconds = await settings.GetIntAsync(
            AppSettingKeys.HeartbeatIntervalSeconds,
            Convert.ToInt32(DefaultHeartbeatInterval.TotalSeconds),
            cancellationToken);
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Clamp(heartbeatSeconds, 5, 300));

        foreach (var openSession in openSessions)
        {
            var game = knownGames.FirstOrDefault(candidate => candidate.Id == openSession.GameId);
            var stillRunning = game is not null
                && game.Executables.Any(executable => runningPathKeys.Contains(executable.ExecutablePathKey))
                && string.Equals(openSession.BootSessionId, currentBootId, StringComparison.Ordinal);

            if (!stillRunning)
            {
                openSession.Close(now);
                await sessions.UpdateAsync(openSession, cancellationToken);
                openByGameId.Remove(openSession.GameId);
                continue;
            }

            if (now - openSession.LastSeenAtUtc >= heartbeatInterval)
            {
                openSession.LastSeenAtUtc = now;
                openSession.BootSessionId = currentBootId;
                await sessions.UpdateAsync(openSession, cancellationToken);
            }
        }

        foreach (var game in knownGames.Where(game =>
                     game.Executables.Any(executable => runningPathKeys.Contains(executable.ExecutablePathKey))))
        {
            if (openByGameId.ContainsKey(game.Id))
            {
                continue;
            }

            var startedAtUtc = sessionStartOverrides.GetValueOrDefault(game.Id, now);
            if (startedAtUtc > now)
            {
                startedAtUtc = now;
            }

            await sessions.AddAsync(new GameSession
            {
                GameId = game.Id,
                StartedAtUtc = startedAtUtc,
                LastSeenAtUtc = now,
                BootSessionId = currentBootId
            }, cancellationToken);
        }
    }

    private async Task RefreshLauncherCatalogIfNeededAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (launcherCatalogUpdatedAtUtc != DateTimeOffset.MinValue && now - launcherCatalogUpdatedAtUtc < LauncherRefreshInterval)
        {
            return;
        }

        try
        {
            launcherCatalog = await installationProvider.DiscoverAsync(cancellationToken);
            launcherCatalogUpdatedAtUtc = now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Launcher discovery failed; registered games will continue to be tracked.");
            launcherCatalogUpdatedAtUtc = now;
        }
    }

    private async Task DiscoverRunningLauncherGamesAsync(
        IReadOnlyList<RunningProcessInfo> runningProcesses,
        DateTimeOffset now,
        IDictionary<long, DateTimeOffset> sessionStartOverrides,
        CancellationToken cancellationToken)
    {
        var seenCandidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in runningProcesses)
        {
            var installation = FindBestInstallation(process);
            if (installation is null)
            {
                continue;
            }

            var isExplicitLaunchExecutable = installation.LaunchExecutablePaths.Any(path =>
                string.Equals(ExecutablePathNormalizer.CreateKey(path), process.ExecutablePathKey, StringComparison.OrdinalIgnoreCase));
            if (!isExplicitLaunchExecutable && IsExcludedHelperExecutable(process.ExecutablePath))
            {
                continue;
            }

            var candidateKey = $"{installation.Source}:{installation.ExternalGameId}:{process.ExecutablePathKey}";
            seenCandidateKeys.Add(candidateKey);
            var firstSeenAtUtc = now;
            var confirmed = isExplicitLaunchExecutable;

            if (!confirmed)
            {
                if (discoveryCandidates.TryGetValue(candidateKey, out var candidate) && candidate.LastSeenScan == scanNumber - 1)
                {
                    firstSeenAtUtc = candidate.FirstSeenAtUtc;
                    confirmed = true;
                }
                else
                {
                    discoveryCandidates[candidateKey] = new DiscoveryCandidate(now, scanNumber);
                }
            }

            if (!confirmed)
            {
                continue;
            }

            var game = await EnsureDiscoveredGameAsync(installation, process, cancellationToken);
            sessionStartOverrides[game.Id] = firstSeenAtUtc;
            discoveryCandidates[candidateKey] = new DiscoveryCandidate(firstSeenAtUtc, scanNumber);
        }

        foreach (var staleKey in discoveryCandidates.Keys.Where(key => !seenCandidateKeys.Contains(key)).ToArray())
        {
            discoveryCandidates.Remove(staleKey);
        }
    }

    private GameInstallationInfo? FindBestInstallation(RunningProcessInfo process)
    {
        return launcherCatalog.Games
            .Select(installation => new
            {
                Installation = installation,
                IsExplicit = installation.LaunchExecutablePaths.Any(path =>
                    string.Equals(ExecutablePathNormalizer.CreateKey(path), process.ExecutablePathKey, StringComparison.OrdinalIgnoreCase))
            })
            .Where(candidate => candidate.IsExplicit || IsPathInside(process.ExecutablePathKey, candidate.Installation.InstallDirectoryKey))
            .OrderByDescending(candidate => candidate.IsExplicit)
            .ThenByDescending(candidate => candidate.Installation.InstallDirectoryKey.Length)
            .Select(candidate => candidate.Installation)
            .FirstOrDefault();
    }

    private async Task<Game> EnsureDiscoveredGameAsync(
        GameInstallationInfo installation,
        RunningProcessInfo process,
        CancellationToken cancellationToken)
    {
        var byExecutable = await games.GetByExecutablePathKeyAsync(process.ExecutablePathKey, cancellationToken);
        if (byExecutable is not null)
        {
            return byExecutable;
        }

        var existing = await games.GetByExternalIdAsync(installation.Source, installation.ExternalGameId, cancellationToken);
        var executable = new GameExecutable
        {
            ExecutablePath = process.ExecutablePath,
            ExecutablePathKey = process.ExecutablePathKey,
            ExecutableName = Path.GetFileName(process.ExecutablePath),
            IsPrimary = existing is null,
            AddedAtUtc = clock.UtcNow
        };

        if (existing is not null)
        {
            await games.AddExecutableAsync(existing.Id, executable, cancellationToken);
            return await games.GetByIdAsync(existing.Id, cancellationToken) ?? existing;
        }

        return await games.AddAsync(new Game
        {
            Name = installation.Name,
            Source = installation.Source,
            ExternalGameId = installation.ExternalGameId,
            InstallDirectory = installation.InstallDirectory,
            InstallDirectoryKey = installation.InstallDirectoryKey,
            AddedAtUtc = clock.UtcNow,
            Executables = [executable]
        }, cancellationToken);
    }

    private static bool IsPathInside(string executablePathKey, string directoryPathKey)
    {
        var prefix = directoryPathKey.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return executablePathKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExcludedHelperExecutable(string executablePath)
    {
        var name = Path.GetFileName(executablePath);
        return ExcludedExecutableParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    private async Task CloseAllOpenSessionsAsync(DateTimeOffset endedAtUtc, CancellationToken cancellationToken)
    {
        var openSessions = await sessions.GetOpenSessionsAsync(cancellationToken);
        foreach (var session in openSessions)
        {
            session.Close(endedAtUtc);
            await sessions.UpdateAsync(session, cancellationToken);
        }
    }

    private async Task PublishStateAsync(bool isRunning, CancellationToken cancellationToken)
    {
        var openSessions = await sessions.GetOpenSessionsAsync(cancellationToken);
        var runningGames = openSessions
            .Where(session => session.Game is not null)
            .Select(session => new RunningGameInfo(
                session.GameId,
                session.Game!.Name,
                session.StartedAtUtc,
                session.GetEffectiveDuration(clock.UtcNow)))
            .OrderBy(info => info.Name)
            .ToArray();

        State = new TrackingState(isRunning, isPaused, runningGames);
        StateChanged?.Invoke(this, State);
    }

    private sealed record DiscoveryCandidate(DateTimeOffset FirstSeenAtUtc, long LastSeenScan);
}
