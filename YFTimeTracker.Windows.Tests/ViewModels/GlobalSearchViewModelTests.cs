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

    private sealed class FakeSearchRepository(GlobalSearchResults results) : IGlobalSearchRepository
    {
        public GlobalSearchResults Results { get; set; } = results;

        public int CallCount { get; private set; }

        public Task<GlobalSearchResults> SearchAsync(
            string searchText,
            int gameCount,
            int sessionCount,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Results);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
