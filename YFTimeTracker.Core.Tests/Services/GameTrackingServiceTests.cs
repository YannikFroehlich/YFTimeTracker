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
            new FakeSuspendNotifier(),
            clock,
            NullLogger<GameTrackingService>.Instance);

        await tracking.ScanOnceAsync(CancellationToken.None);
        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));

        clock.UtcNow = DateTimeOffset.Parse("2026-08-30T10:01:00Z");
        processSnapshot.RunningPathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedSessions = await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None);
        Assert.HasCount(1, storedSessions);
        Assert.IsNotNull(storedSessions[0].EndedAtUtc);
        Assert.AreEqual(60, storedSessions[0].DurationSeconds);
    }

    [TestMethod]
    public async Task Xbox_game_with_explicit_executable_is_imported_on_first_scan()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var executablePath = @"D:\XboxGames\NeonGame\Content\NeonGame.exe";
        var processSnapshot = CreateProcessSnapshot(executablePath);
        var installations = CreateInstallationProvider(
            GameSource.Xbox,
            "Contoso.NeonGame_123",
            "Neon Game",
            @"D:\XboxGames\NeonGame\Content",
            [executablePath]);
        var tracking = CreateTracking(games, sessions, processSnapshot, installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedGames = await games.GetAllAsync(CancellationToken.None);
        Assert.HasCount(1, storedGames);
        Assert.AreEqual(GameSource.Xbox, storedGames[0].Source);
        Assert.AreEqual("Contoso.NeonGame_123", storedGames[0].ExternalGameId);
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

    [TestMethod]
    public async Task Alternative_executables_keep_one_session_open_until_all_processes_exit()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var game = await AddManualGameAsync(
            games,
            "Mehrprozess-Spiel",
            clock.UtcNow,
            @"C:\Games\Multi\game.exe",
            @"C:\Games\Multi\renderer.exe");
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var processes = new FakeProcessSnapshotProvider
        {
            RunningProcesses =
            [
                CreateProcess(@"C:\Games\Multi\game.exe"),
                CreateProcess(@"C:\Games\Multi\renderer.exe")
            ]
        };
        var tracking = CreateTracking(games, sessions, processes, new FakeGameInstallationProvider(), clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        processes.RunningProcesses = [CreateProcess(@"C:\Games\Multi\renderer.exe")];
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));

        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        processes.RunningProcesses = [];
        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedSessions = await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None);
        Assert.HasCount(1, storedSessions);
        Assert.AreEqual(6, storedSessions[0].DurationSeconds);
    }

    [TestMethod]
    public async Task Restarted_game_process_splits_the_session_at_the_last_observed_scan()
    {
        var startedAt = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var clock = new FakeClock(startedAt);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Restart\game.exe";
        var game = await AddManualGameAsync(games, "Neustart-Spiel", clock.UtcNow, path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var processes = new FakeProcessSnapshotProvider
        {
            RunningProcesses = [CreateProcess(path, startedAt.AddMinutes(-1))]
        };
        var tracking = CreateTracking(games, sessions, processes, new FakeGameInstallationProvider(), clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = startedAt.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        clock.UtcNow = startedAt.AddSeconds(8);
        processes.RunningProcesses = [CreateProcess(path, startedAt.AddSeconds(5))];
        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedSessions = (await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None))
            .OrderBy(session => session.StartedAtUtc)
            .ToArray();
        Assert.HasCount(2, storedSessions);
        Assert.AreEqual(startedAt.AddSeconds(3), storedSessions[0].EndedAtUtc);
        Assert.AreEqual(startedAt.AddSeconds(5), storedSessions[1].StartedAtUtc);
        Assert.IsNull(storedSessions[1].EndedAtUtc);
    }

    [TestMethod]
    public async Task Long_scan_gap_splits_running_session_and_excludes_sleep_time()
    {
        var startedAt = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var clock = new FakeClock(startedAt);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Sleep\game.exe";
        var game = await AddManualGameAsync(games, "Standby-Spiel", clock.UtcNow, path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var processes = new FakeProcessSnapshotProvider
        {
            RunningProcesses = [CreateProcess(path, startedAt.AddMinutes(-1))]
        };
        var tracking = CreateTracking(games, sessions, processes, new FakeGameInstallationProvider(), clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = startedAt.AddSeconds(30);
        await tracking.ScanOnceAsync(CancellationToken.None);

        clock.UtcNow = startedAt.AddMinutes(10);
        await tracking.ScanOnceAsync(CancellationToken.None);

        var storedSessions = (await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None))
            .OrderBy(session => session.StartedAtUtc)
            .ToArray();
        Assert.HasCount(2, storedSessions);
        Assert.AreEqual(30, storedSessions[0].DurationSeconds);
        Assert.AreEqual(startedAt.AddMinutes(10), storedSessions[1].StartedAtUtc);
        Assert.IsNull(storedSessions[1].EndedAtUtc);
    }

    [TestMethod]
    public async Task Long_scan_gap_closes_ended_game_at_last_observed_scan()
    {
        var startedAt = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var clock = new FakeClock(startedAt);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\SleepExit\game.exe";
        var game = await AddManualGameAsync(games, "Standby-Ende", clock.UtcNow, path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var processes = CreateProcessSnapshot(path);
        var tracking = CreateTracking(games, sessions, processes, new FakeGameInstallationProvider(), clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = startedAt.AddSeconds(30);
        await tracking.ScanOnceAsync(CancellationToken.None);

        clock.UtcNow = startedAt.AddMinutes(10);
        processes.RunningProcesses = [];
        await tracking.ScanOnceAsync(CancellationToken.None);

        var session = (await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None)).Single();
        Assert.AreEqual(startedAt.AddSeconds(30), session.EndedAtUtc);
        Assert.AreEqual(30, session.DurationSeconds);
    }

    [TestMethod]
    public async Task Recovery_continues_running_game_in_same_boot_without_duplicate_session()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:10:00Z");
        var clock = new FakeClock(now);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Recovery\game.exe";
        var game = await AddManualGameAsync(games, "Recovery", now.AddMinutes(-10), path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var original = await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddMinutes(-10),
            LastSeenAtUtc = now.AddSeconds(-10),
            BootSessionId = "boot"
        }, CancellationToken.None);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            new FakeGameInstallationProvider(),
            clock);

        await tracking.RecoverOpenSessionsAsync(CancellationToken.None);

        var openSession = (await sessions.GetOpenSessionsAsync(CancellationToken.None)).Single();
        Assert.AreEqual(original.Id, openSession.Id);
        Assert.HasCount(1, await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task Recovery_after_reboot_closes_old_session_and_starts_a_new_one()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:10:00Z");
        var clock = new FakeClock(now);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Reboot\game.exe";
        var game = await AddManualGameAsync(games, "Reboot", now.AddMinutes(-10), path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddMinutes(-10),
            LastSeenAtUtc = now.AddMinutes(-2),
            BootSessionId = "old-boot"
        }, CancellationToken.None);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            new FakeGameInstallationProvider(),
            clock,
            new FakeBootSessionProvider("new-boot"));

        await tracking.RecoverOpenSessionsAsync(CancellationToken.None);

        var storedSessions = (await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None))
            .OrderBy(session => session.StartedAtUtc)
            .ToArray();
        Assert.HasCount(2, storedSessions);
        Assert.AreEqual(now.AddMinutes(-2), storedSessions[0].EndedAtUtc);
        Assert.AreEqual(now, storedSessions[1].StartedAtUtc);
        Assert.AreEqual("new-boot", storedSessions[1].BootSessionId);
    }

    [TestMethod]
    public async Task Persisted_pause_closes_stale_session_on_start_and_does_not_reopen_it()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:10:00Z");
        var clock = new FakeClock(now);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Paused\game.exe";
        var game = await AddManualGameAsync(games, "Pausiert", now.AddMinutes(-10), path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddMinutes(-10),
            LastSeenAtUtc = now.AddMinutes(-1),
            BootSessionId = "boot"
        }, CancellationToken.None);
        var settings = new InMemorySettingsStore();
        await settings.SetAsync(AppSettingKeys.TrackingEnabled, bool.FalseString, CancellationToken.None);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            new FakeGameInstallationProvider(),
            clock,
            settings: settings);

        await tracking.StartAsync(CancellationToken.None);
        try
        {
            Assert.IsTrue(tracking.State.IsPaused);
            Assert.IsEmpty(await sessions.GetOpenSessionsAsync(CancellationToken.None));
            var storedSession = (await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None)).Single();
            Assert.AreEqual(now.AddMinutes(-1), storedSession.EndedAtUtc);
        }
        finally
        {
            await tracking.StopAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public async Task Pause_and_resume_split_a_running_game_into_two_sessions()
    {
        var startedAt = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var clock = new FakeClock(startedAt);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\PauseResume\game.exe";
        var game = await AddManualGameAsync(games, "Pause/Resume", clock.UtcNow, path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            new FakeGameInstallationProvider(),
            clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = startedAt.AddSeconds(10);
        await tracking.PauseAsync(CancellationToken.None);
        clock.UtcNow = startedAt.AddSeconds(20);
        await tracking.ResumeAsync(CancellationToken.None);

        var storedSessions = (await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None))
            .OrderBy(session => session.StartedAtUtc)
            .ToArray();
        Assert.HasCount(2, storedSessions);
        Assert.AreEqual(10, storedSessions[0].DurationSeconds);
        Assert.AreEqual(startedAt.AddSeconds(20), storedSessions[1].StartedAtUtc);
        Assert.IsNull(storedSessions[1].EndedAtUtc);
    }

    [TestMethod]
    public async Task Launcher_failure_does_not_interrupt_manually_registered_game()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Manual\game.exe";
        var game = await AddManualGameAsync(games, "Manuell", clock.UtcNow, path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var installations = new FakeGameInstallationProvider { Exception = new IOException("Manifest defekt") };
        var tracking = CreateTracking(games, sessions, CreateProcessSnapshot(path), installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.AreEqual(1, installations.CallCount);
        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Legitimate_game_name_containing_crash_is_not_filtered()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var path = @"C:\Steam\CrashBandicoot\CrashBandicoot4.exe";
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            CreateInstallationProvider(GameSource.Steam, "crash-4", "Crash Bandicoot 4", @"C:\Steam\CrashBandicoot", []),
            clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.HasCount(1, await games.GetAllAsync(CancellationToken.None));
        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Explicit_launcher_helper_is_never_imported_as_a_game()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var path = @"C:\Epic\NeonGame\GameLauncher.exe";
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            CreateInstallationProvider(GameSource.Epic, "neon", "Neon Game", @"C:\Epic\NeonGame", [path]),
            clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.IsEmpty(await games.GetAllAsync(CancellationToken.None));
        Assert.IsEmpty(await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Xbox_game_launch_helper_is_never_imported_as_a_game()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var path = @"D:\XboxGames\NeonGame\Content\gamelaunchhelper.exe";
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            CreateInstallationProvider(
                GameSource.Xbox,
                "Contoso.NeonGame_123",
                "Neon Game",
                @"D:\XboxGames\NeonGame\Content",
                [path]),
            clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.IsEmpty(await games.GetAllAsync(CancellationToken.None));
        Assert.IsEmpty(await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Longest_matching_installation_directory_wins()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var outerDirectory = @"C:\Steam\Common\SpaceGame";
        var innerDirectory = @"C:\Steam\Common\SpaceGame\Definitive";
        var path = innerDirectory + @"\SpaceGame.exe";
        var sources = new Dictionary<GameSource, LauncherAvailability>
        {
            [GameSource.Steam] = LauncherAvailability.Available,
            [GameSource.Epic] = LauncherAvailability.NotInstalled,
            [GameSource.Gog] = LauncherAvailability.NotInstalled
        };
        var installations = new FakeGameInstallationProvider
        {
            Result = new LauncherDiscoveryResult(
            [
                new GameInstallationInfo(GameSource.Steam, "outer", "Space Game", outerDirectory, ExecutablePathNormalizer.CreateKey(outerDirectory), []),
                new GameInstallationInfo(GameSource.Steam, "inner", "Space Game Definitive", innerDirectory, ExecutablePathNormalizer.CreateKey(innerDirectory), [])
            ],
            sources)
        };
        var tracking = CreateTracking(games, sessions, CreateProcessSnapshot(path), installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        await tracking.ScanOnceAsync(CancellationToken.None);

        var game = (await games.GetAllAsync(CancellationToken.None)).Single();
        Assert.AreEqual("inner", game.ExternalGameId);
        Assert.AreEqual("Space Game Definitive", game.Name);
    }

    [TestMethod]
    public async Task Concurrent_scans_create_only_one_open_session()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Concurrent\game.exe";
        var game = await AddManualGameAsync(games, "Parallel", clock.UtcNow, path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            new FakeGameInstallationProvider(),
            clock);

        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => tracking.ScanOnceAsync(CancellationToken.None)));

        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));
        Assert.HasCount(1, await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task Malformed_launcher_path_does_not_block_manual_tracking()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var manualPath = @"C:\Games\ManualSafe\game.exe";
        var game = await AddManualGameAsync(games, "Manuell sicher", clock.UtcNow, manualPath);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var sources = new Dictionary<GameSource, LauncherAvailability>
        {
            [GameSource.Steam] = LauncherAvailability.Available,
            [GameSource.Epic] = LauncherAvailability.NotInstalled,
            [GameSource.Gog] = LauncherAvailability.NotInstalled
        };
        var installations = new FakeGameInstallationProvider
        {
            Result = new LauncherDiscoveryResult(
            [
                new GameInstallationInfo(
                    GameSource.Steam,
                    "broken",
                    "Defekter Eintrag",
                    @"C:\Broken",
                    ExecutablePathNormalizer.CreateKey(@"C:\Broken"),
                    ["\0"])
            ],
            sources)
        };
        var tracking = CreateTracking(games, sessions, CreateProcessSnapshot(manualPath), installations, clock);

        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Faulty_state_listener_does_not_interrupt_tracking()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Listener\game.exe";
        var game = await AddManualGameAsync(games, "Listener", clock.UtcNow, path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            new FakeGameInstallationProvider(),
            clock);
        tracking.StateChanged += (_, _) => throw new InvalidOperationException("Defekter UI-Listener");

        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Duplicate_open_sessions_are_repaired_without_double_counting()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:10:00Z");
        var clock = new FakeClock(now);
        var games = new InMemoryGameRepository();
        var path = @"C:\Games\Duplicate\game.exe";
        var game = await AddManualGameAsync(games, "Doppelte Session", now.AddMinutes(-10), path);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var original = await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddMinutes(-10),
            LastSeenAtUtc = now.AddSeconds(-10),
            BootSessionId = "boot"
        }, CancellationToken.None);
        var duplicate = await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddMinutes(-5),
            LastSeenAtUtc = now.AddSeconds(-10),
            BootSessionId = "boot"
        }, CancellationToken.None);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(path),
            new FakeGameInstallationProvider(),
            clock);

        await tracking.ScanOnceAsync(CancellationToken.None);

        var openSession = (await sessions.GetOpenSessionsAsync(CancellationToken.None)).Single();
        var storedDuplicate = await sessions.GetByIdAsync(duplicate.Id, CancellationToken.None);
        Assert.AreEqual(original.Id, openSession.Id);
        Assert.IsNotNull(storedDuplicate);
        Assert.AreEqual(0, storedDuplicate.DurationSeconds);
    }

    [TestMethod]
    public async Task Suspending_the_system_closes_the_open_session_at_the_suspend_time()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-05T10:00:00Z"));
        var games = new InMemoryGameRepository();
        var game = await AddManualGameAsync(games, "Test", clock.UtcNow, @"C:\Games\Test.exe");
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var suspendNotifier = new FakeSuspendNotifier();
        var settings = new InMemorySettingsStore();
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "60", CancellationToken.None);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(@"C:\Games\Test.exe"),
            new FakeGameInstallationProvider(),
            clock,
            settings: settings,
            suspendNotifier: suspendNotifier);

        await using (tracking)
        {
            await tracking.StartAsync(CancellationToken.None);
            Assert.HasCount(1, await sessions.GetOpenSessionsAsync(CancellationToken.None));

            clock.UtcNow = DateTimeOffset.Parse("2026-09-05T10:00:30Z");
            suspendNotifier.RaiseSuspending();

            Assert.IsEmpty(await sessions.GetOpenSessionsAsync(CancellationToken.None));
            var stored = await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None);
            Assert.HasCount(1, stored);
            Assert.AreEqual(30, stored[0].DurationSeconds);
        }
    }

    [TestMethod]
    public async Task Short_suspension_below_the_gap_threshold_is_not_counted_as_playtime()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-05T10:00:00Z"));
        var games = new InMemoryGameRepository();
        var game = await AddManualGameAsync(games, "Test", clock.UtcNow, @"C:\Games\Test.exe");
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var suspendNotifier = new FakeSuspendNotifier();
        var settings = new InMemorySettingsStore();
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "60", CancellationToken.None);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(@"C:\Games\Test.exe"),
            new FakeGameInstallationProvider(),
            clock,
            settings: settings,
            suspendNotifier: suspendNotifier);

        await using (tracking)
        {
            await tracking.StartAsync(CancellationToken.None);

            clock.UtcNow = DateTimeOffset.Parse("2026-09-05T10:00:30Z");
            suspendNotifier.RaiseSuspending();

            // 30 Sekunden Schlaf liegen unter der Schwelle der Lückenerkennung und wären ohne das
            // Energieereignis vollständig als Spielzeit gezählt worden.
            clock.UtcNow = DateTimeOffset.Parse("2026-09-05T10:01:00Z");
            await tracking.ScanOnceAsync(CancellationToken.None);

            clock.UtcNow = DateTimeOffset.Parse("2026-09-05T10:02:00Z");
            await tracking.StopAsync(CancellationToken.None);

            var stored = (await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None))
                .OrderBy(session => session.StartedAtUtc)
                .ToArray();
            Assert.HasCount(2, stored);
            Assert.AreEqual(30, stored[0].DurationSeconds);
            Assert.AreEqual(DateTimeOffset.Parse("2026-09-05T10:01:00Z"), stored[1].StartedAtUtc);
            Assert.AreEqual(60, stored[1].DurationSeconds);
        }
    }

    [TestMethod]
    public async Task Resuming_the_system_starts_a_new_session_for_a_still_running_game()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-05T10:00:00Z"));
        var games = new InMemoryGameRepository();
        var game = await AddManualGameAsync(games, "Test", clock.UtcNow, @"C:\Games\Test.exe");
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var suspendNotifier = new FakeSuspendNotifier();
        var settings = new InMemorySettingsStore();
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "60", CancellationToken.None);
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(@"C:\Games\Test.exe"),
            new FakeGameInstallationProvider(),
            clock,
            settings: settings,
            suspendNotifier: suspendNotifier);

        await using (tracking)
        {
            await tracking.StartAsync(CancellationToken.None);
            clock.UtcNow = DateTimeOffset.Parse("2026-09-05T10:00:30Z");
            suspendNotifier.RaiseSuspending();
            Assert.IsEmpty(await sessions.GetOpenSessionsAsync(CancellationToken.None));

            clock.UtcNow = DateTimeOffset.Parse("2026-09-05T10:05:00Z");
            suspendNotifier.RaiseResumed();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var openSessions = await sessions.GetOpenSessionsAsync(CancellationToken.None);
            while (openSessions.Count == 0 && !timeout.IsCancellationRequested)
            {
                await Task.Delay(25, CancellationToken.None);
                openSessions = await sessions.GetOpenSessionsAsync(CancellationToken.None);
            }

            Assert.HasCount(1, openSessions);
            Assert.AreEqual(DateTimeOffset.Parse("2026-09-05T10:05:00Z"), openSessions[0].StartedAtUtc);
        }
    }

    [TestMethod]
    public async Task ScanOnceAsync_applies_a_changed_scan_interval()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-05T10:00:00Z"));
        var settings = new InMemorySettingsStore();
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "3", CancellationToken.None);
        var tracking = CreateTracking(
            new InMemoryGameRepository(),
            new InMemoryGameSessionRepository(_ => null),
            new FakeProcessSnapshotProvider(),
            new FakeGameInstallationProvider(),
            clock,
            settings: settings);

        await tracking.ScanOnceAsync(CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromSeconds(3), tracking.ScanInterval);

        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "12", CancellationToken.None);
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.AreEqual(TimeSpan.FromSeconds(12), tracking.ScanInterval);
    }

    [TestMethod]
    public async Task Running_tracking_loop_scans_again_with_a_shortened_interval()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-05T10:00:00Z"));
        var settings = new InMemorySettingsStore();
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "60", CancellationToken.None);
        var processSnapshot = new FakeProcessSnapshotProvider();
        var tracking = CreateTracking(
            new InMemoryGameRepository(),
            new InMemoryGameSessionRepository(_ => null),
            processSnapshot,
            new FakeGameInstallationProvider(),
            clock,
            settings: settings);

        await using (tracking)
        {
            await tracking.StartAsync(CancellationToken.None);
            Assert.AreEqual(TimeSpan.FromSeconds(60), tracking.ScanInterval);

            await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "1", CancellationToken.None);
            await tracking.ScanOnceAsync(CancellationToken.None);
            Assert.AreEqual(TimeSpan.FromSeconds(1), tracking.ScanInterval);

            // Ohne die Übernahme in den laufenden Timer käme der nächste Scan erst nach 60 Sekunden.
            var scansSoFar = processSnapshot.CallCount;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (processSnapshot.CallCount == scansSoFar && !timeout.IsCancellationRequested)
            {
                await Task.Delay(50, CancellationToken.None);
            }

            Assert.IsGreaterThan(
                scansSoFar,
                processSnapshot.CallCount,
                "Der laufende Tracking-Timer hat das verkürzte Scan-Intervall nicht übernommen.");
        }
    }

    [TestMethod]
    public async Task ScanOnceAsync_clamps_the_scan_interval_to_the_supported_range()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-05T10:00:00Z"));
        var settings = new InMemorySettingsStore();
        var tracking = CreateTracking(
            new InMemoryGameRepository(),
            new InMemoryGameSessionRepository(_ => null),
            new FakeProcessSnapshotProvider(),
            new FakeGameInstallationProvider(),
            clock,
            settings: settings);

        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "999", CancellationToken.None);
        await tracking.ScanOnceAsync(CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromSeconds(60), tracking.ScanInterval);

        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "0", CancellationToken.None);
        await tracking.ScanOnceAsync(CancellationToken.None);
        Assert.AreEqual(TimeSpan.FromSeconds(1), tracking.ScanInterval);
    }

    [TestMethod]
    public async Task Paused_scan_applies_a_changed_scan_interval_without_opening_sessions()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-05T10:00:00Z"));
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
        var settings = new InMemorySettingsStore();
        var tracking = CreateTracking(
            games,
            sessions,
            CreateProcessSnapshot(@"C:\Games\Test.exe"),
            new FakeGameInstallationProvider(),
            clock,
            settings: settings);

        await tracking.PauseAsync(CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, "20", CancellationToken.None);
        await tracking.ScanOnceAsync(CancellationToken.None);

        Assert.AreEqual(TimeSpan.FromSeconds(20), tracking.ScanInterval);
        Assert.IsEmpty(await sessions.GetOpenSessionsAsync(CancellationToken.None));
    }

    private static GameTrackingService CreateTracking(
        InMemoryGameRepository games,
        InMemoryGameSessionRepository sessions,
        FakeProcessSnapshotProvider processes,
        FakeGameInstallationProvider installations,
        FakeClock clock,
        FakeBootSessionProvider? bootSession = null,
        InMemorySettingsStore? settings = null,
        FakeSuspendNotifier? suspendNotifier = null)
    {
        return new GameTrackingService(
            games,
            sessions,
            processes,
            installations,
            bootSession ?? new FakeBootSessionProvider("boot"),
            settings ?? new InMemorySettingsStore(),
            suspendNotifier ?? new FakeSuspendNotifier(),
            clock,
            NullLogger<GameTrackingService>.Instance);
    }

    private static FakeProcessSnapshotProvider CreateProcessSnapshot(string executablePath) => new()
    {
        RunningProcesses = [CreateProcess(executablePath)]
    };

    private static RunningProcessInfo CreateProcess(string executablePath, DateTimeOffset? startedAtUtc = null) => new(
        executablePath,
        ExecutablePathNormalizer.CreateKey(executablePath),
        startedAtUtc);

    private static Task<Game> AddManualGameAsync(
        InMemoryGameRepository games,
        string name,
        DateTimeOffset addedAtUtc,
        params string[] executablePaths)
    {
        return games.AddAsync(new Game
        {
            Name = name,
            Source = GameSource.Manual,
            AddedAtUtc = addedAtUtc,
            Executables = executablePaths.Select((path, index) => new GameExecutable
            {
                ExecutablePath = path,
                ExecutablePathKey = ExecutablePathNormalizer.CreateKey(path),
                ExecutableName = Path.GetFileName(path),
                IsPrimary = index == 0,
                AddedAtUtc = addedAtUtc
            }).ToList()
        }, CancellationToken.None);
    }

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
