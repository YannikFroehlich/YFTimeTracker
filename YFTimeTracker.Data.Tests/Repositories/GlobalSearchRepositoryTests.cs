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

        var alphaResults = await repository.SearchAsync(new GlobalSearchQuery("alpha"), 5, 5, CancellationToken.None);
        var percentResults = await repository.SearchAsync(new GlobalSearchQuery("100%"), 5, 5, CancellationToken.None);

        Assert.HasCount(1, alphaResults.Games);
        Assert.AreEqual("Alpha", alphaResults.Games[0].Name);
        Assert.HasCount(1, alphaResults.Sessions);
        Assert.AreEqual(alpha.Id, alphaResults.Sessions[0].GameId);
        Assert.HasCount(1, percentResults.Games);
        Assert.AreEqual("100% Spaß", percentResults.Games[0].Name);
    }

    [TestMethod]
    public async Task Search_supports_typo_launcher_and_session_time_filters()
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
        var steamGame = await AddGameAsync(games, "Cyberpunk", "cyberpunk.exe", now, GameSource.Steam);
        var epicGame = await AddGameAsync(games, "Cyberpunk Arena", "arena.exe", now, GameSource.Epic);
        await sessions.AddAsync(CreateSession(steamGame.Id, now.AddDays(-3)), CancellationToken.None);
        await sessions.AddAsync(CreateSession(steamGame.Id, now.AddDays(-20)), CancellationToken.None);
        await sessions.AddAsync(CreateSession(epicGame.Id, now.AddDays(-1)), CancellationToken.None);

        var results = await repository.SearchAsync(
            new GlobalSearchQuery("cyberpnuk", GameSource.Steam, now.AddDays(-7)),
            5,
            5,
            CancellationToken.None);

        Assert.HasCount(1, results.Games);
        Assert.AreEqual(steamGame.Id, results.Games[0].Id);
        Assert.HasCount(1, results.Sessions);
        Assert.AreEqual(now.AddDays(-3), results.Sessions[0].StartedAtUtc);
    }

    private static Task<Game> AddGameAsync(
        GameRepository games,
        string name,
        string executableName,
        DateTimeOffset now,
        GameSource source = GameSource.Manual)
    {
        return games.AddAsync(new Game
        {
            Name = name,
            Source = source,
            ExecutablePath = $@"C:\Games\{executableName}",
            ExecutablePathKey = $@"C:\GAMES\{executableName.ToUpperInvariant()}",
            ExecutableName = executableName,
            AddedAtUtc = now
        }, CancellationToken.None);
    }

    private static GameSession CreateSession(long gameId, DateTimeOffset startedAtUtc)
    {
        return new GameSession
        {
            GameId = gameId,
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = startedAtUtc.AddHours(1),
            EndedAtUtc = startedAtUtc.AddHours(1),
            DurationSeconds = 3600,
            BootSessionId = "boot"
        };
    }
}
