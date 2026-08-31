using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Repositories;

namespace YFTimeTracker.Data.Tests.Repositories;

[TestClass]
public sealed class PlaytimeReadRepositoryTests
{
    [TestMethod]
    public async Task Overview_aggregates_stored_fallback_and_running_durations_in_sqlite()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var games = new GameRepository(factory);
        var sessions = new GameSessionRepository(factory);
        var repository = new PlaytimeReadRepository(factory);
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var alpha = await AddGameAsync(games, "Alpha", now);
        var beta = await AddGameAsync(games, "Beta", now);

        await AddSessionAsync(sessions, alpha.Id, now.AddHours(-3), now.AddHours(-2), 3600);
        await AddSessionAsync(sessions, beta.Id, now.AddDays(-2), now.AddDays(-2).AddHours(2), null);
        await AddSessionAsync(sessions, alpha.Id, now.AddMinutes(-30), null, null);

        var overview = await repository.GetOverviewAsync(now, recentGameCount: 8, CancellationToken.None);

        Assert.AreEqual(12_600L, overview.TotalDurationSeconds);
        Assert.AreEqual(2, overview.GamesPlayedCount);
        Assert.HasCount(2, overview.RecentGames);
        Assert.AreEqual("Alpha", overview.RecentGames[0].Name);
        Assert.AreEqual(TimeSpan.FromMinutes(90), overview.RecentGames[0].TotalDuration);
        Assert.IsTrue(overview.RecentGames[0].IsRunning);
        Assert.AreEqual(now.AddDays(-2), await repository.GetEarliestSessionStartAsync(CancellationToken.None));
        Assert.AreEqual(12_600L, await repository.GetTotalDurationSecondsAsync(now, CancellationToken.None));
    }

    private static Task<Game> AddGameAsync(GameRepository games, string name, DateTimeOffset now)
    {
        return games.AddAsync(new Game
        {
            Name = name,
            ExecutablePath = $@"C:\Games\{name}.exe",
            ExecutablePathKey = $@"C:\GAMES\{name.ToUpperInvariant()}.EXE",
            ExecutableName = $"{name}.exe",
            AddedAtUtc = now
        }, CancellationToken.None);
    }

    private static Task<GameSession> AddSessionAsync(
        GameSessionRepository sessions,
        long gameId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        long? durationSeconds)
    {
        return sessions.AddAsync(new GameSession
        {
            GameId = gameId,
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = endedAtUtc ?? startedAtUtc,
            EndedAtUtc = endedAtUtc,
            DurationSeconds = durationSeconds,
            BootSessionId = "boot"
        }, CancellationToken.None);
    }
}
