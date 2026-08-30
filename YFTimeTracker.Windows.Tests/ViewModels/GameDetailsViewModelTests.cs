using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class GameDetailsViewModelTests
{
    [TestMethod]
    public async Task Load_builds_real_summary_executables_and_daily_timeline()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var game = CreateGame();
        var firstStart = ToUtc(new DateTime(2026, 8, 30, 22, 30, 0));
        var firstEnd = ToUtc(new DateTime(2026, 8, 31, 0, 30, 0));
        var secondStart = ToUtc(new DateTime(2026, 8, 31, 9, 0, 0));
        var secondEnd = ToUtc(new DateTime(2026, 8, 31, 10, 0, 0));
        var sessions = new FakeSessionRepository(
        [
            CreateSession(1, game, firstStart, firstEnd),
            CreateSession(2, game, secondStart, secondEnd)
        ]);
        var viewModel = CreateViewModel(game, sessions, now);

        await viewModel.LoadAsync(game.Id);

        Assert.AreEqual("Test Game", viewModel.GameName);
        Assert.AreEqual("STEAM", viewModel.SourceLabel);
        Assert.AreEqual("3 h 00 min", viewModel.TotalPlaytimeText);
        Assert.AreEqual("2 Sessions", viewModel.SessionCountText);
        Assert.AreEqual("1 h 30 min", viewModel.AverageSessionText);
        Assert.HasCount(2, viewModel.Executables);
        Assert.IsTrue(viewModel.Executables[0].IsPrimary);
        Assert.HasCount(30, viewModel.Timeline);

        var august30 = viewModel.Timeline.Single(point => point.TooltipText.StartsWith("30.08.2026"));
        var august31 = viewModel.Timeline.Single(point => point.TooltipText.StartsWith("31.08.2026"));
        Assert.AreEqual(TimeSpan.FromMinutes(90), august30.Duration);
        Assert.AreEqual(TimeSpan.FromMinutes(90), august31.Duration);
    }

    [TestMethod]
    public async Task Detail_commands_update_name_and_edit_add_delete_sessions()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var game = CreateGame();
        var storedSession = CreateSession(1, game, now.AddHours(-3), now.AddHours(-2));
        var sessions = new FakeSessionRepository([storedSession]);
        var catalog = new FakeCatalog(game);
        var editor = new FakeSessionEditor(sessions, game);
        var viewModel = new GameDetailsViewModel(
            new FakeGameRepository(game),
            catalog,
            sessions,
            editor,
            new FixedClock(now));

        await viewModel.LoadAsync(game.Id);
        viewModel.GameName = "Neuer Name";
        await viewModel.SaveGameCommand.ExecuteAsync(null);

        Assert.AreEqual(1, catalog.UpdateCallCount);
        Assert.AreEqual("Neuer Name", game.Name);

        viewModel.SelectedSession = viewModel.Sessions.Single();
        viewModel.EditorStartTime = viewModel.EditorStartTime!.Value.Add(TimeSpan.FromMinutes(5));
        await viewModel.SaveSessionCommand.ExecuteAsync(null);
        Assert.AreEqual(1, editor.UpdateCallCount);

        viewModel.NewSessionCommand.Execute(null);
        await viewModel.SaveSessionCommand.ExecuteAsync(null);
        Assert.AreEqual(1, editor.AddCallCount);

        viewModel.SelectedSession = viewModel.Sessions.First(session => session.Id == storedSession.Id);
        await viewModel.DeleteSelectedSessionAsync();
        Assert.AreEqual(1, editor.DeleteCallCount);
    }

    private static GameDetailsViewModel CreateViewModel(Game game, FakeSessionRepository sessions, DateTimeOffset now)
    {
        return new GameDetailsViewModel(
            new FakeGameRepository(game),
            new FakeCatalog(game),
            sessions,
            new FakeSessionEditor(sessions, game),
            new FixedClock(now));
    }

    private static Game CreateGame()
    {
        return new Game
        {
            Id = 7,
            Name = "Test Game",
            Source = GameSource.Steam,
            ExternalGameId = "1234",
            InstallDirectory = @"C:\Games\TestGame",
            AddedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            Executables =
            [
                new GameExecutable
                {
                    Id = 1,
                    GameId = 7,
                    ExecutablePath = @"C:\Games\TestGame\game.exe",
                    ExecutablePathKey = @"C:\GAMES\TESTGAME\GAME.EXE",
                    ExecutableName = "game.exe",
                    IsPrimary = true
                },
                new GameExecutable
                {
                    Id = 2,
                    GameId = 7,
                    ExecutablePath = @"C:\Games\TestGame\game_dx12.exe",
                    ExecutablePathKey = @"C:\GAMES\TESTGAME\GAME_DX12.EXE",
                    ExecutableName = "game_dx12.exe"
                }
            ]
        };
    }

    private static GameSession CreateSession(long id, Game game, DateTimeOffset start, DateTimeOffset end)
    {
        return new GameSession
        {
            Id = id,
            GameId = game.Id,
            Game = game,
            StartedAtUtc = start,
            LastSeenAtUtc = end,
            EndedAtUtc = end,
            DurationSeconds = Convert.ToInt64((end - start).TotalSeconds),
            BootSessionId = "boot"
        };
    }

    private static DateTimeOffset ToUtc(DateTime local)
    {
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeCatalog(Game game) : IGameCatalogService
    {
        public int UpdateCallCount { get; private set; }

        public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Game>>([game]);

        public Task<Game> AddGameAsync(string executablePath, string? displayName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateGameAsync(long gameId, string displayName, string executablePath, CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            game.Name = displayName;
            return Task.CompletedTask;
        }

        public Task DeleteGameAsync(long gameId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeGameRepository(Game game) : IGameRepository
    {
        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Game>>([game]);

        public Task<Game?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(id == game.Id ? game : null);

        public Task<Game?> GetByExecutablePathKeyAsync(string executablePathKey, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(null);

        public Task<Game?> GetByExternalIdAsync(GameSource source, string externalGameId, CancellationToken cancellationToken) =>
            Task.FromResult<Game?>(null);

        public Task<Game> AddAsync(Game newGame, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(Game updatedGame, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<GameExecutable> AddExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetPrimaryExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(long id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSessionEditor(FakeSessionRepository repository, Game game) : IGameSessionEditor
    {
        public int AddCallCount { get; private set; }
        public int UpdateCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }

        public Task<GameSession> AddManualSessionAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken)
        {
            AddCallCount++;
            var session = CreateSession(repository.NextId, game, startedAtUtc, endedAtUtc);
            repository.Items.Add(session);
            return Task.FromResult(session);
        }

        public Task UpdateManualSessionAsync(long sessionId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            var session = repository.Items.Single(candidate => candidate.Id == sessionId);
            session.StartedAtUtc = startedAtUtc;
            session.LastSeenAtUtc = endedAtUtc;
            session.EndedAtUtc = endedAtUtc;
            session.DurationSeconds = Convert.ToInt64((endedAtUtc - startedAtUtc).TotalSeconds);
            return Task.CompletedTask;
        }

        public Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken)
        {
            DeleteCallCount++;
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

        public Task<IReadOnlyList<GameSession>> GetSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(Items.ToArray());

        public Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(Items.Where(session => session.GameId == gameId).ToArray());

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

        public Task<bool> HasOverlapAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, long? excludedSessionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
