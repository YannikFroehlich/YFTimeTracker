using YFTimeTracker.App.Services;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class GamesViewModelTests
{
    [TestMethod]
    public void Xbox_games_have_source_label_and_library_filter()
    {
        var game = CreateGame(1, "Xbox Spiel", GameSource.Xbox, "game.exe");
        var viewModel = CreateViewModel(
            [game],
            new FakeSessionRepository([]),
            new FakeTrackingService(TrackingState.Stopped),
            DateTimeOffset.Parse("2026-08-30T12:00:00Z"));

        Assert.AreEqual("XBOX", new GameListItemViewModel(game).SourceLabel);
        Assert.HasCount(1, viewModel.SourceFilters.Where(filter => filter.Source == GameSource.Xbox));
    }

    [TestMethod]
    public async Task Refresh_combines_sessions_and_tracking_then_filters_and_sorts()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var alpha = CreateGame(1, "Alpha", GameSource.Steam, "alpha.exe");
        var beta = CreateGame(2, "Beta", GameSource.Epic, "beta.exe");
        var gamma = CreateGame(3, "Gamma", GameSource.Manual, "gamma.exe");
        var sessions = new FakeSessionRepository(
        [
            CreateSession(1, alpha, now.AddDays(-2), now.AddDays(-2).AddHours(1)),
            CreateSession(2, beta, now.AddHours(-4), now.AddHours(-1))
        ]);
        var tracking = new FakeTrackingService(new TrackingState(
            true,
            false,
            [new RunningGameInfo(beta.Id, beta.Name, now.AddMinutes(-30), TimeSpan.FromMinutes(30))]));
        var viewModel = CreateViewModel([alpha, beta, gamma], sessions, tracking, now);

        await viewModel.RefreshAsync();

        CollectionAssert.AreEqual(new[] { "Beta", "Alpha", "Gamma" }, viewModel.Games.Select(game => game.Name).ToArray());
        Assert.IsTrue(viewModel.Games[0].IsRunning);
        Assert.AreEqual("3 h 00 min", viewModel.Games[0].TotalPlaytime);

        viewModel.SearchText = "alpha.exe";
        Assert.HasCount(1, viewModel.Games);
        Assert.AreEqual("Alpha", viewModel.Games[0].Name);
        Assert.AreEqual("1 von 3 Spielen", viewModel.ResultSummary);

        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.SelectedSourceFilter = viewModel.SourceFilters.Single(filter => filter.Source == GameSource.Epic);
        Assert.HasCount(1, viewModel.Games);
        Assert.AreEqual("Beta", viewModel.Games[0].Name);

        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.SelectedStatusFilter = viewModel.StatusFilters.Single(filter => filter.Kind == LibraryStatusFilterKind.Running);
        Assert.HasCount(1, viewModel.Games);
        Assert.AreEqual("Beta", viewModel.Games[0].Name);

        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.SelectedStatusFilter = viewModel.StatusFilters.Single(filter => filter.Kind == LibraryStatusFilterKind.MissingExecutable);
        Assert.HasCount(3, viewModel.Games);

        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.SelectedSortOption = viewModel.SortOptions.Single(option => option.Kind == LibrarySortKind.Name);
        CollectionAssert.AreEqual(new[] { "Alpha", "Beta", "Gamma" }, viewModel.Games.Select(game => game.Name).ToArray());
    }

    [TestMethod]
    public async Task Filtering_out_selected_game_clears_editor_state()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var alpha = CreateGame(1, "Alpha", GameSource.Steam, "alpha.exe");
        var beta = CreateGame(2, "Beta", GameSource.Epic, "beta.exe");
        var viewModel = CreateViewModel(
            [alpha, beta],
            new FakeSessionRepository([]),
            new FakeTrackingService(TrackingState.Stopped),
            now);

        await viewModel.RefreshAsync();
        viewModel.SelectedGame = viewModel.Games.Single(game => game.Id == alpha.Id);
        Assert.AreEqual("Alpha", viewModel.DisplayName);

        viewModel.SelectedSourceFilter = viewModel.SourceFilters.Single(filter => filter.Source == GameSource.Epic);

        Assert.IsNull(viewModel.SelectedGame);
        Assert.AreEqual(string.Empty, viewModel.DisplayName);
        Assert.AreEqual(string.Empty, viewModel.ExecutablePath);
    }

    private static GamesViewModel CreateViewModel(
        IReadOnlyList<Game> games,
        FakeSessionRepository sessions,
        FakeTrackingService tracking,
        DateTimeOffset now)
    {
        return new GamesViewModel(
            new FakeCatalog(games),
            sessions,
            new FakeSessionEditor(),
            new FakeFilePicker(),
            tracking,
            new FixedClock(now));
    }

    private static Game CreateGame(long id, string name, GameSource source, string executableName)
    {
        return new Game
        {
            Id = id,
            Name = name,
            Source = source,
            InstallDirectory = $@"C:\MissingGames\{name}",
            AddedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            Executables =
            [
                new GameExecutable
                {
                    Id = id,
                    GameId = id,
                    ExecutablePath = $@"C:\MissingGames\{name}\{executableName}",
                    ExecutablePathKey = $@"C:\MISSINGGAMES\{name.ToUpperInvariant()}\{executableName.ToUpperInvariant()}",
                    ExecutableName = executableName,
                    IsPrimary = true
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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeCatalog(IReadOnlyList<Game> games) : IGameCatalogService
    {
        public Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken) => Task.FromResult(games);

        public Task<Game> AddGameAsync(string executablePath, string? displayName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateGameAsync(long gameId, string displayName, string executablePath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteGameAsync(long gameId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSessionEditor : IGameSessionEditor
    {
        public Task<GameSession> AddManualSessionAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateManualSessionAsync(long sessionId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> PickExecutableAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickExportArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickDiagnosticsArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickYearReviewImageAsync(int year, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickStatisticsExportAsync(string periodLabel, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FakeTrackingService(TrackingState state) : IGameTrackingService
    {
        public TrackingState State { get; private set; } = state;

        public event EventHandler<TrackingState>? StateChanged;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RecoverOpenSessionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ScanOnceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetState(TrackingState newState)
        {
            State = newState;
            StateChanged?.Invoke(this, newState);
        }
    }

    private sealed class FakeSessionRepository(IEnumerable<GameSession> sessions) : IGameSessionRepository
    {
        private readonly IReadOnlyList<GameSession> items = sessions.ToArray();

        public Task<GameSession?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(items.FirstOrDefault(session => session.Id == id));

        public Task<IReadOnlyList<GameSession>> GetOpenSessionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(items.Where(session => session.IsOpen).ToArray());

        public Task<IReadOnlyList<GameSession>> GetSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken) =>
            Task.FromResult(items);

        public Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(items.Where(session => session.GameId == gameId).ToArray());

        public Task<IReadOnlyList<GameSession>> GetRecentCompletedSessionsAsync(int count, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameSession>>(items.Where(session => !session.IsOpen).Take(count).ToArray());

        public Task<GameSession> AddAsync(GameSession session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(GameSession session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(long id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> HasOverlapAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, long? excludedSessionId, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
