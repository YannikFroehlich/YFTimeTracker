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
    ISystemSuspendNotifier suspendNotifier,
    IClock clock,
    ITrackingDiagnosticLog diagnostics,
    ILogger<GameTrackingService> logger) : IGameTrackingService
{
    private const int MinScanIntervalSeconds = 1;
    private const int MaxScanIntervalSeconds = 60;

    // Windows wartet beim Aussetzen nur kurz auf die Anwendung. Kommt das Schließen der Sessions
    // nicht rechtzeitig durch, greift beim nächsten Scan weiterhin die Lückenerkennung.
    private static readonly TimeSpan SuspendHandlingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultScanInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LauncherRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly string[] ExcludedExecutableNames =
    [
        "beservice", "beservicex64", "cefprocess", "crashreportclient", "crashreporter", "eac",
        "eaclauncher", "gamelaunchhelper", "launcher", "reporter", "setup", "startprotectedgame", "uninstall",
        "uninstaller", "update", "updater"
    ];
    private static readonly string[] ExcludedExecutablePrefixes =
    [
        "easyanticheat", "launcher", "unins", "unitycrashhandler"
    ];
    private static readonly string[] ExcludedExecutableSuffixes =
    [
        "launcher", "updater"
    ];
    private static readonly string[] ExcludedExecutableParts =
    [
        "anticheat", "battleye", "crashhandler", "crashreport", "gamelauncher"
    ];

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, DiscoveryCandidate> discoveryCandidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> reportedExcludedCandidates = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? runCancellation;
    private Task? runTask;
    private LauncherDiscoveryResult launcherCatalog = LauncherDiscoveryResult.Empty;
    private DateTimeOffset launcherCatalogUpdatedAtUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? lastSuccessfulScanAtUtc;
    private long scanNumber;
    private bool isPaused;
    private long scanIntervalTicks = DefaultScanInterval.Ticks;
    private PeriodicTimer? scanTimer;

    public TrackingState State { get; private set; } = TrackingState.Stopped;

    public TimeSpan ScanInterval => new(Volatile.Read(ref scanIntervalTicks));

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
            suspendNotifier.Suspending += SuspendNotifier_Suspending;
            suspendNotifier.Resumed += SuspendNotifier_Resumed;
            runTask = RunAsync(runCancellation.Token);
            await PublishStateAsync(true, cancellationToken);
            diagnostics.Record(
                TrackingDiagnosticEventKind.Status,
                TrackingDiagnosticSeverity.Information,
                "Trackingdienst gestartet",
                isPaused ? "Das Tracking ist in den Einstellungen pausiert." : "Spiele werden automatisch überwacht.");
        }
        catch
        {
            suspendNotifier.Suspending -= SuspendNotifier_Suspending;
            suspendNotifier.Resumed -= SuspendNotifier_Resumed;
            if (runCancellation is not null)
            {
                await runCancellation.CancelAsync();
                runCancellation.Dispose();
            }

            runCancellation = null;
            runTask = null;
            throw;
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
            suspendNotifier.Suspending -= SuspendNotifier_Suspending;
            suspendNotifier.Resumed -= SuspendNotifier_Resumed;
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
            reportedExcludedCandidates.Clear();
            await PublishStateAsync(false, cancellationToken);
            diagnostics.Record(
                TrackingDiagnosticEventKind.Status,
                TrackingDiagnosticSeverity.Information,
                "Trackingdienst beendet",
                "Laufende Sessions wurden ordnungsgemäß geschlossen.");
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
            diagnostics.Record(
                TrackingDiagnosticEventKind.Status,
                TrackingDiagnosticSeverity.Warning,
                "Tracking pausiert",
                "Laufende Sessions wurden beendet; während der Pause werden keine Spiele importiert.");
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
            diagnostics.Record(
                TrackingDiagnosticEventKind.Status,
                TrackingDiagnosticSeverity.Success,
                "Tracking fortgesetzt",
                "Ein sofortiger Scan wurde ausgeführt.");
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
            if (isPaused)
            {
                await RefreshScanIntervalAsync(cancellationToken);
            }
            else
            {
                await ScanOnceCoreAsync(cancellationToken);
            }

            await PublishStateAsync(runTask is not null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Record(
                TrackingDiagnosticEventKind.Error,
                TrackingDiagnosticSeverity.Error,
                "Tracking-Scan fehlgeschlagen",
                $"Technischer Fehler: {exception.GetType().Name}. Beim nächsten Intervall wird erneut geprüft.");
            throw;
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

    private void SuspendNotifier_Suspending(object? sender, EventArgs e)
    {
        // Bewusst blockierend: Windows setzt aus, sobald die Behandlung zurückkehrt. Würde hier
        // nur eine Aufgabe gestartet, käme das Schließen der Sessions womöglich nicht mehr durch.
        using var timeout = new CancellationTokenSource(SuspendHandlingTimeout);
        try
        {
            CloseOpenSessionsForSuspendAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Could not close the open sessions before the system suspended; the tracking gap detection will handle them.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Handling the system suspension failed.");
        }
    }

    private void SuspendNotifier_Resumed(object? sender, EventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Interruption,
                    TrackingDiagnosticSeverity.Information,
                    "Windows fortgesetzt",
                    "Nach dem Ruhezustand wird sofort erneut nach laufenden Spielen gesucht.");
                await ScanOnceAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The first scan after the system resumed failed.");
            }
        });
    }

    private async Task CloseOpenSessionsForSuspendAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var suspendedAtUtc = clock.UtcNow;
            await CloseAllOpenSessionsAsync(suspendedAtUtc, cancellationToken);
            discoveryCandidates.Clear();
            lastSuccessfulScanAtUtc = suspendedAtUtc;
            logger.LogInformation("Closed the open sessions at {SuspendedAtUtc} because the system suspends.", suspendedAtUtc);
            diagnostics.Record(
                TrackingDiagnosticEventKind.Interruption,
                TrackingDiagnosticSeverity.Warning,
                "Windows-Ruhezustand erkannt",
                "Laufende Sessions wurden am Beginn des Ruhezustands beendet.");
            await PublishStateAsync(runTask is not null, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = await RefreshScanIntervalAsync(cancellationToken);

        using var timer = new PeriodicTimer(interval);
        Volatile.Write(ref scanTimer, timer);
        try
        {
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
        finally
        {
            Volatile.Write(ref scanTimer, null);
        }
    }

    private async Task<TimeSpan> RefreshScanIntervalAsync(CancellationToken cancellationToken)
    {
        var intervalSeconds = await settings.GetIntAsync(
            AppSettingKeys.TrackingIntervalSeconds,
            Convert.ToInt32(DefaultScanInterval.TotalSeconds),
            cancellationToken);
        var interval = TimeSpan.FromSeconds(
            Math.Clamp(intervalSeconds, MinScanIntervalSeconds, MaxScanIntervalSeconds));

        if (Interlocked.Exchange(ref scanIntervalTicks, interval.Ticks) != interval.Ticks)
        {
            ApplyScanInterval(interval);
        }

        return interval;
    }

    private void ApplyScanInterval(TimeSpan interval)
    {
        if (Volatile.Read(ref scanTimer) is not { } timer)
        {
            return;
        }

        try
        {
            timer.Period = interval;
            logger.LogInformation(
                "Applied the new tracking scan interval of {IntervalSeconds} seconds.",
                interval.TotalSeconds);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RecoverOpenSessionsCoreAsync(CancellationToken cancellationToken)
    {
        var openSessions = await ResolveDuplicateOpenSessionsAsync(
            await sessions.GetOpenSessionsAsync(cancellationToken),
            cancellationToken);

        if (isPaused)
        {
            foreach (var session in openSessions)
            {
                session.Close(session.LastSeenAtUtc);
                await sessions.UpdateAsync(session, cancellationToken);
                logger.LogInformation(
                    "Closed stale session {SessionId} for game {GameId} because tracking starts paused.",
                    session.Id,
                    session.GameId);
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Session,
                    TrackingDiagnosticSeverity.Warning,
                    "Offene Session geschlossen",
                    $"{GetGameName(session)} wurde beim pausierten App-Start am letzten Lebenszeichen beendet.");
            }

            return;
        }

        if (openSessions.Count == 0)
        {
            await ScanOnceCoreAsync(cancellationToken);
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
                logger.LogInformation(
                    "Recovered session {SessionId} for game {GameId} at its last heartbeat {EndedAtUtc}.",
                    session.Id,
                    session.GameId,
                    session.EndedAtUtc);
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Session,
                    TrackingDiagnosticSeverity.Warning,
                    "Offene Session wiederhergestellt",
                    $"{GetGameName(session)} lief nicht mehr und wurde am letzten Lebenszeichen beendet.");
            }
            else
            {
                logger.LogInformation(
                    "Continued open session {SessionId} for game {GameId} after application restart.",
                    session.Id,
                    session.GameId);
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Session,
                    TrackingDiagnosticSeverity.Success,
                    "Session fortgesetzt",
                    $"{GetGameName(session)} läuft seit dem vorherigen App-Start weiter.");
            }
        }

        await ScanOnceCoreAsync(cancellationToken);
    }

    private async Task ScanOnceCoreAsync(CancellationToken cancellationToken)
    {
        scanNumber++;
        var now = clock.UtcNow;
        var previousSuccessfulScanAtUtc = lastSuccessfulScanAtUtc;
        if (previousSuccessfulScanAtUtc > now)
        {
            logger.LogWarning(
                "The system clock moved backwards from {PreviousScanAtUtc} to {CurrentScanAtUtc}; resetting scan continuity.",
                previousSuccessfulScanAtUtc,
                now);
            previousSuccessfulScanAtUtc = now;
        }

        var runningProcesses = await processSnapshotProvider.GetRunningProcessesAsync(cancellationToken);
        var runningPathKeys = runningProcesses.Select(process => process.ExecutablePathKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sessionStartOverrides = new Dictionary<long, DateTimeOffset>();
        var scanInterval = await RefreshScanIntervalAsync(cancellationToken);
        var suspendGapThreshold = TimeSpan.FromSeconds(Math.Max(120, scanInterval.TotalSeconds * 3));
        var hasUnobservedGap = previousSuccessfulScanAtUtc is { } previousScan
            && now - previousScan > suspendGapThreshold;

        if (hasUnobservedGap)
        {
            discoveryCandidates.Clear();
            logger.LogInformation(
                "Detected an unobserved tracking gap from {PreviousScanAtUtc} to {CurrentScanAtUtc}; sleep time will not be counted.",
                previousSuccessfulScanAtUtc,
                now);
            diagnostics.Record(
                TrackingDiagnosticEventKind.Interruption,
                TrackingDiagnosticSeverity.Warning,
                "Tracking-Unterbrechung erkannt",
                "Zwischen zwei Scans lag eine größere Lücke; diese Zeit wird nicht als Spielzeit gezählt.");
        }

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
        var openSessions = await ResolveDuplicateOpenSessionsAsync(
            await sessions.GetOpenSessionsAsync(cancellationToken),
            cancellationToken);
        var openByGameId = openSessions.ToDictionary(session => session.GameId);
        var heartbeatSeconds = await settings.GetIntAsync(
            AppSettingKeys.HeartbeatIntervalSeconds,
            Convert.ToInt32(DefaultHeartbeatInterval.TotalSeconds),
            cancellationToken);
        var heartbeatInterval = TimeSpan.FromSeconds(Math.Clamp(heartbeatSeconds, 5, 300));

        foreach (var openSession in openSessions)
        {
            var game = knownGames.FirstOrDefault(candidate => candidate.Id == openSession.GameId);
            var matchingProcesses = game is null
                ? []
                : GetMatchingProcesses(game, runningProcesses);
            var sameBootSession = string.Equals(openSession.BootSessionId, currentBootId, StringComparison.Ordinal);
            var stillRunning = matchingProcesses.Count > 0
                && sameBootSession;

            if (hasUnobservedGap)
            {
                var endedAtUtc = previousSuccessfulScanAtUtc!.Value < openSession.StartedAtUtc
                    ? openSession.StartedAtUtc
                    : previousSuccessfulScanAtUtc.Value;
                openSession.Close(endedAtUtc);
                await sessions.UpdateAsync(openSession, cancellationToken);
                openByGameId.Remove(openSession.GameId);
                logger.LogInformation(
                    "Split session {SessionId} for game {GameId} at {EndedAtUtc} after an unobserved tracking gap.",
                    openSession.Id,
                    openSession.GameId,
                    endedAtUtc);
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Interruption,
                    TrackingDiagnosticSeverity.Warning,
                    "Session wegen Unterbrechung geteilt",
                    $"{game?.Name ?? GetGameName(openSession)} wird nach dem nächsten bestätigten Scan als neue Session fortgesetzt.");
                continue;
            }

            var processWasRestarted = stillRunning
                && previousSuccessfulScanAtUtc is { } lastScan
                && matchingProcesses.All(process =>
                    process.StartedAtUtc is { } processStartedAtUtc && processStartedAtUtc > lastScan);

            if (processWasRestarted)
            {
                openSession.Close(previousSuccessfulScanAtUtc!.Value);
                await sessions.UpdateAsync(openSession, cancellationToken);
                openByGameId.Remove(openSession.GameId);
                sessionStartOverrides[openSession.GameId] = ClampSessionStart(
                    matchingProcesses.Min(process => process.StartedAtUtc!.Value),
                    previousSuccessfulScanAtUtc.Value,
                    now);
                logger.LogInformation(
                    "Split session {SessionId} for game {GameId} because all associated processes restarted.",
                    openSession.Id,
                    openSession.GameId);
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Session,
                    TrackingDiagnosticSeverity.Information,
                    "Prozessneustart erkannt",
                    $"Die Session von {game?.Name ?? GetGameName(openSession)} wurde geteilt, weil alle zugeordneten Prozesse neu gestartet sind.");
                continue;
            }

            if (!stillRunning)
            {
                openSession.Close(now);
                await sessions.UpdateAsync(openSession, cancellationToken);
                openByGameId.Remove(openSession.GameId);
                logger.LogInformation(
                    "Closed session {SessionId} for game {GameId}; no associated process is running.",
                    openSession.Id,
                    openSession.GameId);
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Session,
                    TrackingDiagnosticSeverity.Success,
                    "Session beendet",
                    $"{game?.Name ?? GetGameName(openSession)} · {FormatDiagnosticDuration(TimeSpan.FromSeconds(openSession.DurationSeconds ?? 0))} · kein zugeordneter Prozess läuft mehr.");
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
            if (!sessionStartOverrides.ContainsKey(game.Id) && previousSuccessfulScanAtUtc is { } lastScan)
            {
                var matchingProcesses = GetMatchingProcesses(game, runningProcesses);
                var observedStarts = matchingProcesses
                    .Where(process => process.StartedAtUtc is not null)
                    .Select(process => process.StartedAtUtc!.Value)
                    .ToArray();
                if (observedStarts.Length == matchingProcesses.Count && observedStarts.Length > 0)
                {
                    var earliestStart = observedStarts.Min();
                    if (earliestStart > lastScan)
                    {
                        startedAtUtc = ClampSessionStart(earliestStart, lastScan, now);
                    }
                }
            }

            startedAtUtc = ClampSessionStart(startedAtUtc, DateTimeOffset.MinValue, now);
            var session = await sessions.AddAsync(new GameSession
            {
                GameId = game.Id,
                StartedAtUtc = startedAtUtc,
                LastSeenAtUtc = now,
                BootSessionId = currentBootId
            }, cancellationToken);
            logger.LogInformation(
                "Started session {SessionId} for game {GameId} at {StartedAtUtc}.",
                session.Id,
                session.GameId,
                session.StartedAtUtc);
            var executableNames = GetMatchingProcesses(game, runningProcesses)
                .Select(process => Path.GetFileName(process.ExecutablePath))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            diagnostics.Record(
                TrackingDiagnosticEventKind.Session,
                TrackingDiagnosticSeverity.Success,
                "Session gestartet",
                $"{game.Name} · {string.Join(", ", executableNames)}");
        }

        lastSuccessfulScanAtUtc = now;
    }

    private async Task RefreshLauncherCatalogIfNeededAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (launcherCatalogUpdatedAtUtc != DateTimeOffset.MinValue
            && now >= launcherCatalogUpdatedAtUtc
            && now - launcherCatalogUpdatedAtUtc < LauncherRefreshInterval)
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
            diagnostics.Record(
                TrackingDiagnosticEventKind.Error,
                TrackingDiagnosticSeverity.Warning,
                "Launcher-Suche fehlgeschlagen",
                $"Registrierte Spiele werden weiter erkannt. Technischer Fehler: {exception.GetType().Name}.");
        }
    }

    private async Task DiscoverRunningLauncherGamesAsync(
        IReadOnlyList<RunningProcessInfo> runningProcesses,
        DateTimeOffset now,
        IDictionary<long, DateTimeOffset> sessionStartOverrides,
        CancellationToken cancellationToken)
    {
        var seenCandidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenExcludedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in runningProcesses)
        {
            var installation = FindBestInstallation(process);
            if (installation is null)
            {
                continue;
            }

            var isExplicitLaunchExecutable = installation.LaunchExecutablePaths.Any(path =>
                IsSameExecutablePath(path, process.ExecutablePathKey));
            var candidateKey = $"{installation.Source}:{installation.ExternalGameId}:{process.ExecutablePathKey}";
            if (IsExcludedHelperExecutable(process.ExecutablePath))
            {
                seenExcludedKeys.Add(candidateKey);
                if (reportedExcludedCandidates.Add(candidateKey))
                {
                    diagnostics.Record(
                        TrackingDiagnosticEventKind.Exclusion,
                        TrackingDiagnosticSeverity.Information,
                        "Hilfsprozess ausgeschlossen",
                        $"{Path.GetFileName(process.ExecutablePath)} wurde bei {installation.Name} nicht als Spiel gewertet.");
                }

                continue;
            }

            seenCandidateKeys.Add(candidateKey);
            var firstSeenAtUtc = now;
            var confirmed = isExplicitLaunchExecutable;
            var wasConfirmed = discoveryCandidates.GetValueOrDefault(candidateKey)?.IsConfirmed ?? false;

            if (!confirmed)
            {
                if (discoveryCandidates.TryGetValue(candidateKey, out var candidate) && candidate.LastSeenScan == scanNumber - 1)
                {
                    firstSeenAtUtc = candidate.FirstSeenAtUtc;
                    confirmed = true;
                }
                else
                {
                    discoveryCandidates[candidateKey] = new DiscoveryCandidate(now, scanNumber, false);
                    diagnostics.Record(
                        TrackingDiagnosticEventKind.Detection,
                        TrackingDiagnosticSeverity.Information,
                        "Möglicher Spielprozess erkannt",
                        $"{Path.GetFileName(process.ExecutablePath)} passt zu {installation.Name} und wird beim nächsten Scan bestätigt.");
                }
            }

            if (!confirmed)
            {
                continue;
            }

            if (!wasConfirmed)
            {
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Assignment,
                    TrackingDiagnosticSeverity.Success,
                    "Spielprozess zugeordnet",
                    $"{Path.GetFileName(process.ExecutablePath)} wurde {installation.Name} zugeordnet.");
            }

            var game = await EnsureDiscoveredGameAsync(installation, process, cancellationToken);
            sessionStartOverrides[game.Id] = firstSeenAtUtc;
            discoveryCandidates[candidateKey] = new DiscoveryCandidate(firstSeenAtUtc, scanNumber, true);
        }

        foreach (var staleKey in discoveryCandidates.Keys.Where(key => !seenCandidateKeys.Contains(key)).ToArray())
        {
            discoveryCandidates.Remove(staleKey);
        }

        foreach (var staleKey in reportedExcludedCandidates.Where(key => !seenExcludedKeys.Contains(key)).ToArray())
        {
            reportedExcludedCandidates.Remove(staleKey);
        }
    }

    private GameInstallationInfo? FindBestInstallation(RunningProcessInfo process)
    {
        return launcherCatalog.Games
            .Select(installation => new
            {
                Installation = installation,
                IsExplicit = installation.LaunchExecutablePaths.Any(path =>
                    IsSameExecutablePath(path, process.ExecutablePathKey))
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
            logger.LogInformation(
                "Added executable {ExecutableName} to discovered game {GameId} ({Source}:{ExternalGameId}).",
                executable.ExecutableName,
                existing.Id,
                installation.Source,
                installation.ExternalGameId);
            diagnostics.Record(
                TrackingDiagnosticEventKind.Assignment,
                TrackingDiagnosticSeverity.Success,
                "Weitere EXE zugeordnet",
                $"{executable.ExecutableName} wurde {existing.Name} hinzugefügt.");
            return await games.GetByIdAsync(existing.Id, cancellationToken) ?? existing;
        }

        var game = await games.AddAsync(new Game
        {
            Name = installation.Name,
            Source = installation.Source,
            ExternalGameId = installation.ExternalGameId,
            InstallDirectory = installation.InstallDirectory,
            InstallDirectoryKey = installation.InstallDirectoryKey,
            AddedAtUtc = clock.UtcNow,
            Executables = [executable]
        }, cancellationToken);
        logger.LogInformation(
            "Discovered game {GameId} ({Source}:{ExternalGameId}) from running executable {ExecutableName}.",
            game.Id,
            installation.Source,
            installation.ExternalGameId,
            executable.ExecutableName);
        diagnostics.Record(
            TrackingDiagnosticEventKind.Detection,
            TrackingDiagnosticSeverity.Success,
            "Spiel in die Bibliothek übernommen",
            $"{game.Name} wurde über {FormatGameSource(game.Source)} erkannt ({executable.ExecutableName}).");
        return game;
    }

    private static bool IsPathInside(string executablePathKey, string directoryPathKey)
    {
        if (string.IsNullOrWhiteSpace(executablePathKey) || string.IsNullOrWhiteSpace(directoryPathKey))
        {
            return false;
        }

        var prefix = directoryPathKey.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return executablePathKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameExecutablePath(string path, string executablePathKey)
    {
        try
        {
            return string.Equals(
                ExecutablePathNormalizer.CreateKey(path),
                executablePathKey,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsExcludedHelperExecutable(string executablePath)
    {
        var name = new string(Path.GetFileNameWithoutExtension(executablePath)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return ExcludedExecutableNames.Contains(name, StringComparer.Ordinal)
            || ExcludedExecutablePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))
            || ExcludedExecutableSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal))
            || ExcludedExecutableParts.Any(part => name.Contains(part, StringComparison.Ordinal));
    }

    private async Task CloseAllOpenSessionsAsync(DateTimeOffset endedAtUtc, CancellationToken cancellationToken)
    {
        var openSessions = await ResolveDuplicateOpenSessionsAsync(
            await sessions.GetOpenSessionsAsync(cancellationToken),
            cancellationToken);
        foreach (var session in openSessions)
        {
            session.Close(endedAtUtc);
            await sessions.UpdateAsync(session, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<GameSession>> ResolveDuplicateOpenSessionsAsync(
        IReadOnlyList<GameSession> openSessions,
        CancellationToken cancellationToken)
    {
        var resolved = new List<GameSession>(openSessions.Count);
        foreach (var group in openSessions.GroupBy(session => session.GameId))
        {
            var ordered = group.OrderBy(session => session.StartedAtUtc).ThenBy(session => session.Id).ToArray();
            resolved.Add(ordered[0]);
            foreach (var duplicate in ordered.Skip(1))
            {
                duplicate.Close(duplicate.StartedAtUtc);
                await sessions.UpdateAsync(duplicate, cancellationToken);
                logger.LogWarning(
                    "Closed duplicate open session {SessionId} for game {GameId} with zero duration.",
                    duplicate.Id,
                    duplicate.GameId);
                diagnostics.Record(
                    TrackingDiagnosticEventKind.Error,
                    TrackingDiagnosticSeverity.Warning,
                    "Doppelte offene Session korrigiert",
                    $"Für {GetGameName(duplicate)} blieb nur die ältere laufende Session geöffnet.");
            }
        }

        return resolved;
    }

    private static IReadOnlyList<RunningProcessInfo> GetMatchingProcesses(
        Game game,
        IReadOnlyList<RunningProcessInfo> runningProcesses)
    {
        var executableKeys = game.Executables
            .Select(executable => executable.ExecutablePathKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return runningProcesses
            .Where(process => executableKeys.Contains(process.ExecutablePathKey))
            .ToArray();
    }

    private static DateTimeOffset ClampSessionStart(
        DateTimeOffset startedAtUtc,
        DateTimeOffset minimumUtc,
        DateTimeOffset nowUtc)
    {
        if (startedAtUtc < minimumUtc)
        {
            return minimumUtc;
        }

        return startedAtUtc > nowUtc ? nowUtc : startedAtUtc;
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
                session.GetEffectiveDuration(clock.UtcNow),
                session.Game.PrimaryExecutable?.ExecutablePath))
            .OrderBy(info => info.Name)
            .ToArray();

        State = new TrackingState(isRunning, isPaused, runningGames);
        if (StateChanged is not { } stateChanged)
        {
            return;
        }

        foreach (EventHandler<TrackingState> handler in stateChanged.GetInvocationList())
        {
            try
            {
                handler(this, State);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A tracking state listener failed.");
            }
        }
    }

    private static string GetGameName(GameSession session) => session.Game?.Name ?? $"Spiel #{session.GameId}";

    private static string FormatDiagnosticDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{(int)duration.TotalHours} h {duration.Minutes:00} min"
        : $"{Math.Max(0, (int)duration.TotalMinutes)} min";

    private static string FormatGameSource(GameSource source) => source switch
    {
        GameSource.Steam => "Steam",
        GameSource.Epic => "Epic Games",
        GameSource.Gog => "GOG",
        GameSource.Xbox => "Xbox/Microsoft Store",
        GameSource.BattleNet => "Battle.net",
        GameSource.Ubisoft => "Ubisoft Connect",
        GameSource.EaApp => "EA app",
        _ => "eine manuelle Zuordnung"
    };

    private sealed record DiscoveryCandidate(DateTimeOffset FirstSeenAtUtc, long LastSeenScan, bool IsConfirmed);
}
