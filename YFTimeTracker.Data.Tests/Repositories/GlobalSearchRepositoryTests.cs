using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Repositories;

namespace YFTimeTracker.Data.Tests.Repositories;

[TestClass]
public sealed class GlobalSearchRepositoryTests
{
    [TestMethod]
    public async Task Search_finds_games_and_sessions_and_treats_wildcards_as_text()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var games = new GameRepository(factory);
        var sessions = new GameSessionRepository(factory);
        var repository = new GlobalSearchRepository(factory);
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var alpha = await AddGameAsync(games, "Alpha", "alpha.exe", now);
        await AddGameAsync(games, "100% Spaß", "fun.exe", now);
        await sessions.AddAsync(new GameSession
        {
            GameId = alpha.Id,
            StartedAtUtc = now.AddHours(-2),
            LastSeenAtUtc = now.AddHours(-1),
            EndedAtUtc = now.AddHours(-1),
            DurationSeconds = 3600,
            BootSessionId = "boot"
        }, CancellationToken.None);

        var alphaResults = await repository.SearchAsync("alpha", 5, 5, CancellationToken.None);
        var percentResults = await repository.SearchAsync("100%", 5, 5, CancellationToken.None);

        Assert.HasCount(1, alphaResults.Games);
        Assert.AreEqual("Alpha", alphaResults.Games[0].Name);
        Assert.HasCount(1, alphaResults.Sessions);
        Assert.AreEqual(alpha.Id, alphaResults.Sessions[0].GameId);
        Assert.HasCount(1, percentResults.Games);
        Assert.AreEqual("100% Spaß", percentResults.Games[0].Name);
    }

    private static Task<Game> AddGameAsync(
        GameRepository games,
        string name,
        string executableName,
        DateTimeOffset now)
    {
        return games.AddAsync(new Game
        {
            Name = name,
            ExecutablePath = $@"C:\Games\{executableName}",
            ExecutablePathKey = $@"C:\GAMES\{executableName.ToUpperInvariant()}",
            ExecutableName = executableName,
            AddedAtUtc = now
        }, CancellationToken.None);
    }
}
