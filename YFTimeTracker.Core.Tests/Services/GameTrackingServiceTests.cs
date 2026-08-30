using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class GameTrackingServiceTests
{
    [TestMethod]
    public async Task ScanOnceAsync_creates_and_closes_sessions_from_process_snapshot()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T10:00:00Z"));
        var games = new InMemoryGameRepository();
        var game = await games.AddAsync(new Game
        {
            Name = "Test",
            ExecutablePath = @"C:\Games\Test.exe",
            ExecutablePathKey = @"C:\GAMES\TEST.EXE",
            ExecutableName = "Test.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var processSnapshot = new FakeProcessSnapshotProvider
        {
            RunningPathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { game.ExecutablePathKey }
        };
        var tracking = new GameTrackingService(
            games,
            sessions,
            processSnapshot,
            new FakeGameInstallationProvider(),
            new FakeBootSessionProvider("boot"),
            new InMemorySettingsStore(),
            clock,
            NullLogger<GameTrackingService>.Instance);

        await tracking.ScanOnceAsync(CancellationToken.None);
        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));

        clock.UtcNow = DateTimeOffset.Parse("2026-08-30T10:20:00Z");
        processSnapshot.RunningPathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedSessions = await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None);
        Assert.HasCount(1, storedSessions);
        Assert.IsNotNull(storedSessions[0].EndedAtUtc);
        Assert.AreEqual(1200, storedSessions[0].DurationSeconds);
    }

    [TestMethod]
    public async Task Launcher_game_with_explicit_executable_is_imported_on_first_scan()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var executablePath = @"C:\Epic\NeonGame\NeonGame.exe";
        var processSnapshot = CreateProcessSnapshot(executablePath);
        var installations = CreateInstallationProvider(
            GameSource.Epic,
            "catalog-1",
            "Neon Game",
            @"C:\Epic\NeonGame",
            [executablePath]);
        var tracking = CreateTracking(games, sessions, processSnapshot, installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedGames = await games.GetAllAsync(CancellationToken.None);
        Assert.HasCount(1, storedGames);
        Assert.AreEqual(GameSource.Epic, storedGames[0].Source);
        Assert.AreEqual("catalog-1", storedGames[0].ExternalGameId);
        Assert.HasCount(1, storedGames[0].Executables);
        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Launcher_folder_fallback_requires_two_scans_and_preserves_first_seen_time()
    {
        var firstSeen = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var clock = new FakeClock(firstSeen);
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var processSnapshot = CreateProcessSnapshot(@"C:\Steam\steamapps\common\NeonGame\bin\game.exe");
        var installations = CreateInstallationProvider(
            GameSource.Steam,
            "42",
            "Neon Game",
            @"C:\Steam\steamapps\common\NeonGame",
            []);
        var tracking = CreateTracking(games, sessions, processSnapshot, installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        Assert.IsEmpty(await games.GetAllAsync(CancellationToken.None));

        clock.UtcNow = firstSeen.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        var openSession = (await sessions.GetOpenSessionsAsync(CancellationToken.None)).Single();
        Assert.AreEqual(firstSeen, openSession.StartedAtUtc);
    }

    [TestMethod]
    public async Task Launcher_helper_process_is_not_imported()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var processSnapshot = CreateProcessSnapshot(@"C:\Steam\steamapps\common\NeonGame\UnityCrashHandler64.exe");
        var installations = CreateInstallationProvider(
            GameSource.Steam,
            "42",
            "Neon Game",
            @"C:\Steam\steamapps\common\NeonGame",
            []);
        var tracking = CreateTracking(games, sessions, processSnapshot, installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.IsEmpty(await games.GetAllAsync(CancellationToken.None));
        Assert.IsEmpty(await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Multiple_processes_for_one_launcher_game_create_one_game_and_one_session()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var processSnapshot = new FakeProcessSnapshotProvider
        {
            RunningProcesses =
            [
                CreateProcess(@"C:\GOG\NeonGame\game.exe"),
                CreateProcess(@"C:\GOG\NeonGame\bin\renderer.exe")
            ]
        };
        var installations = CreateInstallationProvider(GameSource.Gog, "gog-7", "Neon Game", @"C:\GOG\NeonGame", []);
        var tracking = CreateTracking(games, sessions, processSnapshot, installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedGame = (await games.GetAllAsync(CancellationToken.None)).Single();
        Assert.HasCount(2, storedGame.Executables);
        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Paused_tracking_does_not_import_launcher_games()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var executablePath = @"C:\Epic\NeonGame\NeonGame.exe";
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(executablePath),
            CreateInstallationProvider(GameSource.Epic, "catalog-1", "Neon Game", @"C:\Epic\NeonGame", [executablePath]),
            clock);

        await tracking.PauseAsync(CancellationToken.None);
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.IsEmpty(await games.GetAllAsync(CancellationToken.None));
    }

    private static GameTrackingService CreateTracking(
        InMemoryGameRepository games,
        InMemoryGameSessionRepository sessions,
        FakeProcessSnapshotProvider processes,
        FakeGameInstallationProvider installations,
        FakeClock clock)
    {
        return new GameTrackingService(
            games,
            sessions,
            processes,
            installations,
            new FakeBootSessionProvider("boot"),
            new InMemorySettingsStore(),
            clock,
            NullLogger<GameTrackingService>.Instance);
    }

    private static FakeProcessSnapshotProvider CreateProcessSnapshot(string executablePath) => new()
    {
        RunningProcesses = [CreateProcess(executablePath)]
    };

    private static RunningProcessInfo CreateProcess(string executablePath) => new(
        executablePath,
        ExecutablePathNormalizer.CreateKey(executablePath));

    private static FakeGameInstallationProvider CreateInstallationProvider(
        GameSource source,
        string externalId,
        string name,
        string installDirectory,
        IReadOnlyList<string> launchExecutables)
    {
        var sources = new Dictionary<GameSource, LauncherAvailability>
        {
            [GameSource.Steam] = LauncherAvailability.NotInstalled,
            [GameSource.Epic] = LauncherAvailability.NotInstalled,
            [GameSource.Gog] = LauncherAvailability.NotInstalled,
            [source] = LauncherAvailability.Available
        };
        return new FakeGameInstallationProvider
        {
            Result = new LauncherDiscoveryResult(
                [new GameInstallationInfo(
                    source,
                    externalId,
                    name,
                    installDirectory,
                    ExecutablePathNormalizer.CreateKey(installDirectory),
                    launchExecutables)],
                sources)
        };
    }
}
