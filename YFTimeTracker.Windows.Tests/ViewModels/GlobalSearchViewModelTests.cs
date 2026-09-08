using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class GlobalSearchViewModelTests
{
    [TestMethod]
    public async Task Search_builds_game_session_and_navigation_results()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var game = new Game
        {
            Id = 7,
            Name = "Alpha",
            Source = GameSource.Steam,
            Executables =
            [
                new GameExecutable
                {
                    GameId = 7,
                    ExecutableName = "alpha.exe",
                    ExecutablePath = @"C:\Games\Alpha\alpha.exe",
                    ExecutablePathKey = @"C:\GAMES\ALPHA\ALPHA.EXE",
                    IsPrimary = true
                }
            ]
        };
        var session = new GameSession
        {
            Id = 42,
            GameId = game.Id,
            Game = game,
            StartedAtUtc = now.AddHours(-2),
            LastSeenAtUtc = now.AddHours(-1),
            EndedAtUtc = now.AddHours(-1),
            DurationSeconds = 3600,
            BootSessionId = "boot"
        };
        var repository = new FakeSearchRepository(new GlobalSearchResults([game], [session]));
        var viewModel = new GlobalSearchViewModel(repository, new FixedClock(now));

        await viewModel.SearchAsync("Alpha", CancellationToken.None);

        Assert.HasCount(2, viewModel.Results);
        Assert.AreEqual(GlobalSearchResultKind.Game, viewModel.Results[0].Kind);
        Assert.AreEqual(7L, viewModel.Results[0].GameId);
        Assert.AreEqual("Steam · alpha.exe", viewModel.Results[0].Subtitle);
        Assert.AreEqual(GlobalSearchResultKind.Session, viewModel.Results[1].Kind);
        Assert.AreEqual(42L, viewModel.Results[1].SessionId);

        repository.Results = GlobalSearchResults.Empty;
        await viewModel.SearchAsync("Statistik", CancellationToken.None);

        Assert.HasCount(1, viewModel.Results);
        Assert.AreEqual(GlobalSearchResultKind.Statistics, viewModel.Results[0].Kind);

        await viewModel.SearchAsync("Rückblick", CancellationToken.None);

        Assert.HasCount(1, viewModel.Results);
        Assert.AreEqual(GlobalSearchResultKind.YearReview, viewModel.Results[0].Kind);
    }

    [TestMethod]
    public async Task Search_ignores_single_character_queries()
    {
        var repository = new FakeSearchRepository(GlobalSearchResults.Empty);
        var viewModel = new GlobalSearchViewModel(repository, new FixedClock(DateTimeOffset.UtcNow));

        await viewModel.SearchAsync("a", CancellationToken.None);

        Assert.AreEqual(0, repository.CallCount);
        Assert.IsEmpty(viewModel.Results);
    }

    [TestMethod]
    public async Task Search_attaches_local_icon_path_to_game_and_session_results()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var game = new Game
        {
            Id = 7,
            Name = "Alpha",
            Executables =
            [
                new GameExecutable
                {
                    GameId = 7,
                    ExecutableName = "alpha.exe",
                    ExecutablePath = @"C:\Games\Alpha\alpha.exe",
                    IsPrimary = true
                }
            ]
        };
        var session = new GameSession
        {
            Id = 42,
            GameId = game.Id,
            Game = game,
            StartedAtUtc = now.AddHours(-1),
            LastSeenAtUtc = now,
            EndedAtUtc = now,
            DurationSeconds = 3600,
            BootSessionId = "boot"
        };
        var iconService = new FakeGameIconService(@"C:\Cache\alpha.png");
        var viewModel = new GlobalSearchViewModel(
            new FakeSearchRepository(new GlobalSearchResults([game], [session])),
            new FixedClock(now),
            iconService);

        await viewModel.SearchAsync("Alpha", CancellationToken.None);

        Assert.AreEqual(@"C:\Cache\alpha.png", viewModel.Results[0].IconPath);
        Assert.AreEqual(@"C:\Cache\alpha.png", viewModel.Results[1].IconPath);
        Assert.AreEqual("A", viewModel.Results[0].IconText);
        Assert.AreEqual(2, iconService.CallCount);
    }

    [TestMethod]
    public async Task Search_forwards_launcher_and_relative_time_filters()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var repository = new FakeSearchRepository(GlobalSearchResults.Empty);
        var viewModel = new GlobalSearchViewModel(repository, new FixedClock(now));

        await viewModel.SearchAsync(
            "Alpha",
            GameSource.Steam,
            TimeSpan.FromDays(30),
            CancellationToken.None);

        Assert.IsNotNull(repository.LastQuery);
        Assert.AreEqual("Alpha", repository.LastQuery.SearchText);
        Assert.AreEqual(GameSource.Steam, repository.LastQuery.Source);
        Assert.AreEqual(now.AddDays(-30), repository.LastQuery.SessionsSinceUtc);
    }

    [TestMethod]
    public async Task Recent_searches_are_loaded_deduplicated_saved_and_cleared()
    {
        var settings = new FakeSettingsStore
        {
            Value = "[\"Alpha\",\"alpha\",\"Beta\"]"
        };
        var viewModel = new GlobalSearchViewModel(
            new FakeSearchRepository(GlobalSearchResults.Empty),
            new FixedClock(DateTimeOffset.UtcNow),
            settings: settings);

        await viewModel.ShowRecentSearchesAsync(CancellationToken.None);

        Assert.HasCount(2, viewModel.Results);
        Assert.AreEqual(GlobalSearchResultKind.RecentSearch, viewModel.Results[0].Kind);
        Assert.AreEqual("Alpha", viewModel.Results[0].SearchText);

        await viewModel.RememberSearchAsync("Gamma", CancellationToken.None);

        StringAssert.StartsWith(settings.Value, "[\"Gamma\",\"Alpha\",\"Beta\"]");

        await viewModel.ClearRecentSearchesAsync(CancellationToken.None);

        Assert.AreEqual("[]", settings.Value);

        await viewModel.ShowRecentSearchesAsync(CancellationToken.None);

        Assert.IsEmpty(viewModel.Results);
    }

    private sealed class FakeSearchRepository(GlobalSearchResults results) : IGlobalSearchRepository
    {
        public GlobalSearchResults Results { get; set; } = results;

        public int CallCount { get; private set; }

        public GlobalSearchQuery? LastQuery { get; private set; }

        public Task<GlobalSearchResults> SearchAsync(
            GlobalSearchQuery query,
            int gameCount,
            int sessionCount,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult(Results);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeGameIconService(string iconPath) : IGameIconService
    {
        public int CallCount { get; private set; }

        public Task<string?> GetIconPathAsync(string? executablePath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<string?>(iconPath);
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public string? Value { get; set; }

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Value);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            Value = value;
            return Task.CompletedTask;
        }

        public Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken) =>
            Task.FromResult(fallback);

        public Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken) =>
            Task.FromResult(fallback);
    }
}
