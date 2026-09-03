using YFTimeTracker.App.Services;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class SessionsViewModelTests
{
    [TestMethod]
    public async Task Refresh_calculates_real_summary_and_filters_by_game()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var alpha = CreateGame(1, "Alpha");
        var beta = CreateGame(2, "Beta");
        var repository = new FakeSessionRepository(
        [
            CreateCompletedSession(1, alpha, now.AddHours(-3), now.AddHours(-2)),
            CreateCompletedSession(2, beta, now.AddHours(-2), now)
        ]);
        var viewModel = CreateViewModel([alpha, beta], repository, now);

        await viewModel.RefreshAsync();

        Assert.HasCount(2, viewModel.Sessions);
        Assert.AreEqual("3 h 00 min", viewModel.TotalDurationText);
        Assert.AreEqual("1 h 30 min", viewModel.AverageDurationText);

        viewModel.SelectedGameFilter = viewModel.GameFilters.Single(filter => filter.GameId == alpha.Id);

        Assert.HasCount(1, viewModel.Sessions);
        Assert.AreEqual("Alpha", viewModel.Sessions[0].GameName);

        viewModel.SearchText = "nicht vorhanden";
        Assert.IsEmpty(viewModel.Sessions);
    }

    [TestMethod]
    public async Task Running_session_is_visible_but_read_only()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var game = CreateGame(1, "Live-Spiel");
        var repository = new FakeSessionRepository(
        [
            new GameSession
            {
                Id = 1,
                GameId = game.Id,
                Game = game,
                StartedAtUtc = now.AddMinutes(-30),
                LastSeenAtUtc = now,
                BootSessionId = "boot"
            }
        ]);
        var viewModel = CreateViewModel([game], repository, now);

        await viewModel.RefreshAsync();
        viewModel.SelectedSession = viewModel.Sessions.Single();

        Assert.IsFalse(viewModel.EditorFieldsEnabled);
        Assert.IsFalse(viewModel.EditorCanSave);
        Assert.IsFalse(viewModel.CanDeleteSelected);
        Assert.AreEqual("AKTIV", viewModel.SelectedSession.StatusText);
    }

    [TestMethod]
    public async Task Save_command_adds_manual_session_and_refreshes_list()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var game = CreateGame(1, "Manuelles Spiel");
        var repository = new FakeSessionRepository([]);
        var editor = new FakeSessionEditor(repository, id => id == game.Id ? game : null);
        var viewModel = new SessionsViewModel(
            new FakeCatalog([game]),
            repository,
            editor,
            new FixedClock(now),
            new FakeFilePicker(),
            new FakeExplorerService());

        await viewModel.RefreshAsync();
        await viewModel.SaveSessionCommand.ExecuteAsync(null);

        Assert.AreEqual(1, editor.AddCallCount);
        Assert.HasCount(1, viewModel.Sessions);
        Assert.AreEqual("Manuelles Spiel", viewModel.Sessions[0].GameName);
        Assert.AreEqual("Session hinzugefügt", viewModel.StatusMessage);
    }

    [TestMethod]
    public async Task ShowSession_loads_all_time_and_selects_old_session()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var game = CreateGame(1, "Archivspiel");
        var oldSession = CreateCompletedSession(9, game, now.AddDays(-90), now.AddDays(-90).AddHours(2));
        var viewModel = CreateViewModel([game], new FakeSessionRepository([oldSession]), now);

        await viewModel.ShowSessionAsync(oldSession.Id);

        Assert.AreEqual(SessionPeriodKind.All, viewModel.SelectedPeriod.Kind);
        Assert.AreEqual(oldSession.Id, viewModel.SelectedSession?.Id);
        Assert.AreEqual("Archivspiel", viewModel.SelectedSession?.GameName);
    }

    [TestMethod]
    public async Task ExportCsv_writes_visible_sessions_to_picked_path()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var game = CreateGame(1, "Alpha");
        var session = CreateCompletedSession(1, game, now.AddHours(-2), now.AddHours(-1));
        var repository = new FakeSessionRepository([session]);
        var exportPath = Path.Combine(Path.GetTempPath(), $"yftimetracker-sessions-test-{Guid.NewGuid():N}.csv");
        var filePicker = new FakeFilePicker(exportPath);
        var explorerService = new FakeExplorerService();
        var viewModel = CreateViewModel([game], repository, now, filePicker, explorerService);

        await viewModel.RefreshAsync();

        try
        {
            Assert.IsTrue(viewModel.IsExportEnabled);

            await viewModel.ExportCsvCommand.ExecuteAsync(null);

            var lines = await File.ReadAllLinesAsync(exportPath);
            Assert.AreEqual("Spiel;Quelle;Start;Ende;Dauer;Status", lines[0]);
            StringAssert.Contains(lines[1], "Alpha;MANUELL;");
            StringAssert.Contains(lines[1], "1 h 00 min;ABGESCHLOSSEN");
            StringAssert.Contains(viewModel.StatusMessage, Path.GetFileName(exportPath));
            Assert.IsTrue(viewModel.IsExportFolderAvailable);

            viewModel.OpenExportFolderCommand.Execute(null);
            Assert.AreEqual(exportPath, explorerService.RevealedPath);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [TestMethod]
    public async Task ExportCsv_is_disabled_without_sessions()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var game = CreateGame(1, "Alpha");
        var repository = new FakeSessionRepository([]);
        var exportPath = Path.Combine(Path.GetTempPath(), "should-not-be-created.csv");
        var filePicker = new FakeFilePicker(exportPath);
        var viewModel = CreateViewModel([game], repository, now, filePicker);

        await viewModel.RefreshAsync();
        await viewModel.ExportCsvCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsExportEnabled);
        Assert.IsFalse(viewModel.IsExportFolderAvailable);
        Assert.IsFalse(File.Exists(exportPath));
    }

    private static SessionsViewModel CreateViewModel(
        IReadOnlyList<Game> games,
        FakeSessionRepository repository,
        DateTimeOffset now,
        IFilePickerService? filePicker = null,
        IExplorerService? explorerService = null)
    {
        return new SessionsViewModel(
            new FakeCatalog(games),
            repository,
            new FakeSessionEditor(repository, id => games.FirstOrDefault(game => game.Id == id)),
            new FixedClock(now),
            filePicker ?? new FakeFilePicker(),
            explorerService ?? new FakeExplorerService());
    }

    private static Game CreateGame(long id, string name)
    {
        return new Game
        {
            Id = id,
            Name = name,
            AddedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            Executables =
            [
                new GameExecutable
                {
                    Id = id,
                    GameId = id,
                    ExecutablePath = $@"C:\Games\{name}.exe",
                    ExecutablePathKey = $@"C:\GAMES\{name.ToUpperInvariant()}.EXE",
                    ExecutableName = $"{name}.exe",
                    IsPrimary = true
                }
            ]
        };
    }

    private static GameSession CreateCompletedSession(
        long id,
        Game game,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc)
    {
        return new GameSession
        {
            Id = id,
            GameId = game.Id,
            Game = game,
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = endedAtUtc,
            EndedAtUtc = endedAtUtc,
            DurationSeconds = Convert.ToInt64((endedAtUtc - startedAtUtc).TotalSeconds),
            BootSessionId = "boot"
        };
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeCatalog(IReadOnlyList<Game> games) : IGameCatalogService
    {
        public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken) => Task.FromResult(games);

        public Task<Game> AddGameAsync(string executablePath, string? displayName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateGameAsync(long gameId, string displayName, string executablePath, int? dailyPlaytimeLimitMinutes, int? weeklyPlaytimeLimitMinutes, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteGameAsync(long gameId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSessionEditor(
        FakeSessionRepository repository,
        Func<long, Game?> gameResolver) : IGameSessionEditor
    {
        public int AddCallCount { get; private set; }

        public Task<GameSession> AddManualSessionAsync(
            long gameId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset endedAtUtc,
            CancellationToken cancellationToken)
        {
            AddCallCount++;
            var session = new GameSession
            {
                Id = repository.NextId,
                GameId = gameId,
                Game = gameResolver(gameId),
                StartedAtUtc = startedAtUtc,
                LastSeenAtUtc = endedAtUtc,
                EndedAtUtc = endedAtUtc,
                DurationSeconds = Convert.ToInt64((endedAtUtc - startedAtUtc).TotalSeconds),
                BootSessionId = "boot"
            };
            repository.Items.Add(session);
            return Task.FromResult(session);
        }

        public Task UpdateManualSessionAsync(long sessionId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken)
        {
            var session = repository.Items.Single(item => item.Id == sessionId);
            session.StartedAtUtc = startedAtUtc;
            session.LastSeenAtUtc = endedAtUtc;
            session.EndedAtUtc = endedAtUtc;
            session.DurationSeconds = Convert.ToInt64((endedAtUtc - startedAtUtc).TotalSeconds);
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken)
        {
            repository.Items.RemoveAll(session => session.Id == sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionRepository(IEnumerable<GameSession> sessions) : IGameSessionRepository
    {
        public List<GameSession> Items { get; } = sessions.ToList();

        public long NextId => Items.Count == 0 ? 1 : Items.Max(session => session.Id) + 1;

        public Task<GameSession?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(session => session.Id == id));

        public Task<IReadOnlyList<GameSession>> GetOpenSessionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(Items.Where(session => session.IsOpen).ToArray());

        public Task<IReadOnlyList<GameSession>> GetSessionsAsync(
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            CancellationToken cancellationToken)
        {
            var query = Items.AsEnumerable();
            if (fromUtc is { } from)
            {
                query = query.Where(session => (session.EndedAtUtc ?? session.LastSeenAtUtc) > from);
            }

            if (toUtc is { } to)
            {
                query = query.Where(session => session.StartedAtUtc < to);
            }

            return Task.FromResult<IReadOnlyList<GameSession>>(query.ToArray());
        }

        public Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(Items.Where(session => session.GameId == gameId).ToArray());

        public Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(Items
                .Where(session => session.GameId == gameId)
                .Where(session => (session.EndedAtUtc ?? session.LastSeenAtUtc) > fromUtc)
                .Where(session => session.StartedAtUtc < toUtc)
                .ToArray());

        public Task<IReadOnlyList<GameSession>> GetRecentCompletedSessionsAsync(int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(Items.Where(session => !session.IsOpen).Take(count).ToArray());

        public Task<GameSession> AddAsync(GameSession session, CancellationToken cancellationToken)
        {
            Items.Add(session);
            return Task.FromResult(session);
        }

        public Task UpdateAsync(GameSession session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(long id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(session => session.Id == id);
            return Task.CompletedTask;
        }

        public Task<bool> HasOverlapAsync(
            long gameId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset endedAtUtc,
            long? excludedSessionId,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class FakeFilePicker(string? exportPath = null) : IFilePickerService
    {
        public Task<string?> PickExecutableAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickExportArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickDiagnosticsArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickYearReviewImageAsync(int year, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickStatisticsExportAsync(string periodLabel, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickSessionsExportAsync(string periodLabel, CancellationToken cancellationToken) => Task.FromResult(exportPath);
    }

    private sealed class FakeExplorerService : IExplorerService
    {
        public string? RevealedPath { get; private set; }

        public void RevealFile(string path) => RevealedPath = path;
    }
}
