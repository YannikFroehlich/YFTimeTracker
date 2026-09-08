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
    public async Task SessionRepository_reports_overlap_with_open_session()
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
            BootSessionId = "boot"
        }, CancellationToken.None);

        var overlaps = await sessions.HasOverlapAsync(
            game.Id,
            DateTimeOffset.Parse("2026-08-30T10:30:00Z"),
            DateTimeOffset.Parse("2026-08-30T11:30:00Z"),
            null,
            CancellationToken.None);

        Assert.IsTrue(overlaps);
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

    [TestMethod]
    public async Task Migration_leaves_playtime_limits_null_for_pre_existing_games()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260831120000_AddReadModelIndexes");
            var addedAtUtc = DateTimeOffset.Parse("2026-08-31T10:00:00Z").UtcTicks;
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Games (Name, ExecutablePath, ExecutablePathKey, ExecutableName, Source, AddedAtUtc) VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                "Pre-migration Game", @"C:\Games\Existing.exe", @"C:\GAMES\EXISTING.EXE", "Existing.exe", 0, addedAtUtc);
            await migrator.MigrateAsync();
        }

        var repository = new GameRepository(factory);
        var game = (await repository.GetAllAsync(CancellationToken.None)).Single();
        Assert.AreEqual("Pre-migration Game", game.Name);
        Assert.IsNull(game.DailyPlaytimeLimitMinutes);
        Assert.IsNull(game.WeeklyPlaytimeLimitMinutes);
    }

    [TestMethod]
    public async Task MergeInto_moves_sessions_and_executables_and_drops_the_source_game()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var games = new GameRepository(factory);
        var sessions = new GameSessionRepository(factory);
        var addedAt = DateTimeOffset.Parse("2026-09-08T09:00:00Z");

        var target = await games.AddAsync(new Game
        {
            Name = "Beta",
            Source = GameSource.Steam,
            ExecutablePath = @"C:\Games\Beta\beta.exe",
            ExecutablePathKey = @"C:\GAMES\BETA\BETA.EXE",
            ExecutableName = "beta.exe",
            AddedAtUtc = addedAt
        }, CancellationToken.None);

        var source = await games.AddAsync(new Game
        {
            Name = "Beta (manuell)",
            Source = GameSource.Manual,
            ExecutablePath = @"C:\Games\Beta\launch.exe",
            ExecutablePathKey = @"C:\GAMES\BETA\LAUNCH.EXE",
            ExecutableName = "launch.exe",
            AddedAtUtc = addedAt
        }, CancellationToken.None);
        await games.AddExecutableAsync(source.Id, new GameExecutable
        {
            ExecutablePath = @"C:\Games\Beta\bin\render.exe",
            ExecutablePathKey = @"C:\GAMES\BETA\BIN\RENDER.EXE",
            ExecutableName = "render.exe",
            AddedAtUtc = addedAt
        }, CancellationToken.None);

        var keptTarget = await sessions.AddAsync(Closed(target.Id, "10:00", "11:30"), CancellationToken.None);
        var overlapping = await sessions.AddAsync(Closed(source.Id, "11:00", "12:00"), CancellationToken.None);
        var standalone = await sessions.AddAsync(Closed(source.Id, "20:00", "21:00"), CancellationToken.None);

        var plan = YFTimeTracker.Core.Services.SessionMergePlanner.Create(
            await sessions.GetSessionsForGameAsync(target.Id, CancellationToken.None),
            await sessions.GetSessionsForGameAsync(source.Id, CancellationToken.None));

        await games.MergeIntoAsync(source.Id, target.Id, plan, CancellationToken.None);

        Assert.IsNull(await games.GetByIdAsync(source.Id, CancellationToken.None), "Das Quellspiel muss weg sein.");

        var merged = await games.GetByIdAsync(target.Id, CancellationToken.None);
        Assert.IsNotNull(merged);
        Assert.AreEqual("Beta", merged.Name);
        Assert.AreEqual(GameSource.Steam, merged.Source);
        Assert.HasCount(3, merged.Executables);
        Assert.HasCount(1, merged.Executables.Where(executable => executable.IsPrimary));
        Assert.AreEqual(@"C:\GAMES\BETA\BETA.EXE", merged.PrimaryExecutable?.ExecutablePathKey);

        // Der Cascade auf GameSessions darf die umgehängten Sessions nicht mitnehmen.
        var mergedSessions = await sessions.GetSessionsForGameAsync(target.Id, CancellationToken.None);
        Assert.HasCount(2, mergedSessions);
        Assert.IsNull(await sessions.GetByIdAsync(overlapping.Id, CancellationToken.None));

        var combined = mergedSessions.Single(session => session.Id == keptTarget.Id);
        Assert.AreEqual(At("10:00"), combined.StartedAtUtc);
        Assert.AreEqual(At("12:00"), combined.EndedAtUtc);
        Assert.AreEqual(7200, combined.DurationSeconds);

        var moved = mergedSessions.Single(session => session.Id == standalone.Id);
        Assert.AreEqual(target.Id, moved.GameId);
    }

    private static GameSession Closed(long gameId, string start, string end) => new()
    {
        GameId = gameId,
        StartedAtUtc = At(start),
        LastSeenAtUtc = At(end),
        EndedAtUtc = At(end),
        DurationSeconds = (long)(At(end) - At(start)).TotalSeconds,
        BootSessionId = "boot"
    };

    private static DateTimeOffset At(string time) => DateTimeOffset.Parse($"2026-09-08T{time}:00Z");
}
