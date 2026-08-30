using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Repositories;

namespace YFTimeTracker.Data.Tests.Repositories;

[TestClass]
public sealed class GameRepositoryTests
{
    [TestMethod]
    public async Task GameRepository_persists_games_and_cascades_sessions()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var games = new GameRepository(factory);
        var sessions = new GameSessionRepository(factory);
        var game = await games.AddAsync(new Game
        {
            Name = "Test",
            ExecutablePath = @"C:\Games\Test.exe",
            ExecutablePathKey = @"C:\GAMES\TEST.EXE",
            ExecutableName = "Test.exe",
            AddedAtUtc = DateTimeOffset.Parse("2026-08-30T10:00:00Z")
        }, CancellationToken.None);

        await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = DateTimeOffset.Parse("2026-08-30T10:00:00Z"),
            LastSeenAtUtc = DateTimeOffset.Parse("2026-08-30T11:00:00Z"),
            EndedAtUtc = DateTimeOffset.Parse("2026-08-30T11:00:00Z"),
            DurationSeconds = 3600,
            BootSessionId = "boot"
        }, CancellationToken.None);

        Assert.HasCount(1, await games.GetAllAsync(CancellationToken.None));
        Assert.HasCount(1, await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None));

        await games.DeleteAsync(game.Id, CancellationToken.None);

        Assert.IsEmpty(await games.GetAllAsync(CancellationToken.None));
        Assert.IsEmpty(await sessions.GetSessionsForGameAsync(game.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task Database_rejects_two_open_sessions_for_same_game()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var games = new GameRepository(factory);
        var sessions = new GameSessionRepository(factory);
        var game = await games.AddAsync(new Game
        {
            Name = "Test",
            ExecutablePath = @"C:\Games\Test.exe",
            ExecutablePathKey = @"C:\GAMES\TEST.EXE",
            ExecutableName = "Test.exe",
            AddedAtUtc = DateTimeOffset.Parse("2026-08-30T10:00:00Z")
        }, CancellationToken.None);

        await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = DateTimeOffset.Parse("2026-08-30T10:00:00Z"),
            LastSeenAtUtc = DateTimeOffset.Parse("2026-08-30T10:00:00Z"),
            BootSessionId = "boot"
        }, CancellationToken.None);

        try
        {
            await sessions.AddAsync(new GameSession
            {
                GameId = game.Id,
                StartedAtUtc = DateTimeOffset.Parse("2026-08-30T10:05:00Z"),
                LastSeenAtUtc = DateTimeOffset.Parse("2026-08-30T10:05:00Z"),
                BootSessionId = "boot"
            }, CancellationToken.None);
            Assert.Fail("Expected SQLite to reject a second open session for the same game.");
        }
        catch (DbUpdateException)
        {
        }
    }

    [TestMethod]
    public async Task Repository_persists_launcher_identity_and_multiple_executables()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new GameRepository(factory);
        var game = await repository.AddAsync(new Game
        {
            Name = "Launcher Game",
            Source = GameSource.Steam,
            ExternalGameId = "42",
            InstallDirectory = @"C:\Steam\Game",
            InstallDirectoryKey = @"C:\STEAM\GAME",
            ExecutablePath = @"C:\Steam\Game\game.exe",
            ExecutablePathKey = @"C:\STEAM\GAME\GAME.EXE",
            ExecutableName = "game.exe",
            AddedAtUtc = DateTimeOffset.Parse("2026-08-30T10:00:00Z")
        }, CancellationToken.None);

        await repository.AddExecutableAsync(game.Id, new GameExecutable
        {
            ExecutablePath = @"C:\Steam\Game\bin\renderer.exe",
            ExecutablePathKey = @"C:\STEAM\GAME\BIN\RENDERER.EXE",
            ExecutableName = "renderer.exe",
            AddedAtUtc = DateTimeOffset.Parse("2026-08-30T10:01:00Z")
        }, CancellationToken.None);

        var stored = await repository.GetByExternalIdAsync(GameSource.Steam, "42", CancellationToken.None);
        Assert.IsNotNull(stored);
        Assert.HasCount(2, stored.Executables);
        Assert.HasCount(1, stored.Executables.Where(executable => executable.IsPrimary));
    }

    [TestMethod]
    public async Task Migration_moves_v01_executable_into_primary_mapping()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260830012000_InitialCreate");
            var addedAtUtc = DateTimeOffset.Parse("2026-08-30T10:00:00Z").UtcTicks;
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Games (Name, ExecutablePath, ExecutablePathKey, ExecutableName, AddedAtUtc) VALUES ({0}, {1}, {2}, {3}, {4})",
                "Legacy Game", @"C:\Games\Legacy.exe", @"C:\GAMES\LEGACY.EXE", "Legacy.exe", addedAtUtc);
            await migrator.MigrateAsync();
        }

        var repository = new GameRepository(factory);
        var game = (await repository.GetAllAsync(CancellationToken.None)).Single();
        Assert.AreEqual(GameSource.Manual, game.Source);
        Assert.HasCount(1, game.Executables);
        Assert.IsTrue(game.Executables[0].IsPrimary);
        Assert.AreEqual(@"C:\GAMES\LEGACY.EXE", game.Executables[0].ExecutablePathKey);
    }
}
