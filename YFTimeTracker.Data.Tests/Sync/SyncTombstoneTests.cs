using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Repositories;
using TestRepositories = YFTimeTracker.Data.Tests.Repositories;

namespace YFTimeTracker.Data.Tests.Sync;

/// <summary>
/// Sichert ab, dass Loeschungen im Konto ankommen.
///
/// Ohne Grabstein waere "fehlt lokal" beim naechsten Abgleich nicht von "wurde
/// auf dem anderen PC neu angelegt" zu unterscheiden - das geloeschte Spiel kaeme
/// zurueck. Genau das pruefen diese Tests.
/// </summary>
[TestClass]
public sealed class SyncTombstoneTests
{
    private static async Task<(TestRepositories.TempAppPathProvider Paths, TestRepositories.TestDbContextFactory Factory, TestRepositories.TestClock Clock)>
        CreateDatabaseAsync()
    {
        var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        return (paths, factory, new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-15T20:00:00Z")));
    }

    private static async Task<Game> AddSyncedGameAsync(
        TestRepositories.TestDbContextFactory factory,
        TestRepositories.TestClock clock,
        string name = "Testspiel")
    {
        var game = await new GameRepository(factory).AddAsync(new Game
        {
            Name = name,
            ExecutablePath = $@"C:\Games\{name}\game.exe",
            ExecutablePathKey = $@"C:\GAMES\{name.ToUpperInvariant()}\GAME.EXE",
            ExecutableName = "game.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        // So sieht ein Datensatz aus, der schon einmal abgeglichen wurde.
        await using var context = factory.CreateDbContext();
        var stored = await context.Games.FirstAsync(item => item.Id == game.Id);
        stored.CloudIdentity = $"game:manual:{name.ToLowerInvariant()}";
        stored.CloudId = "cloud-game-1";
        stored.SyncedHash = "hash";

        foreach (var executable in await context.GameExecutables.Where(item => item.GameId == game.Id).ToListAsync())
        {
            executable.CloudIdentity = $"exe:{stored.CloudIdentity}:x";
            executable.SyncedHash = "hash";
        }

        await context.SaveChangesAsync();
        return game;
    }

    [TestMethod]
    public async Task Deleting_a_synced_game_leaves_a_tombstone()
    {
        var (paths, factory, clock) = await CreateDatabaseAsync();
        using var _ = paths;

        var game = await AddSyncedGameAsync(factory, clock);
        await new GameRepository(factory).DeleteAsync(game.Id, CancellationToken.None);

        await using var context = factory.CreateDbContext();
        var tombstones = await context.SyncTombstones.ToListAsync();

        Assert.IsTrue(
            tombstones.Any(item => item.Kind == SyncEntityKind.Game && item.Identity == "game:manual:testspiel"),
            "Ohne Grabstein käme das gelöschte Spiel beim nächsten Abgleich zurück.");
    }

    [TestMethod]
    public async Task Deleting_a_game_also_tombstones_what_the_cascade_removes()
    {
        var (paths, factory, clock) = await CreateDatabaseAsync();
        using var _ = paths;

        var game = await AddSyncedGameAsync(factory, clock);
        await new GameRepository(factory).DeleteAsync(game.Id, CancellationToken.None);

        await using var context = factory.CreateDbContext();
        var kinds = await context.SyncTombstones.Select(item => item.Kind).ToListAsync();

        // Die EXE-Zuordnung verschwindet lokal per Fremdschlüssel mit. Ohne eigenen
        // Grabstein bliebe sie im Konto als Waise stehen.
        CollectionAssert.Contains(kinds, SyncEntityKind.Executable);
    }

    [TestMethod]
    public async Task Deleting_a_game_that_was_never_synced_leaves_no_tombstone()
    {
        var (paths, factory, clock) = await CreateDatabaseAsync();
        using var _ = paths;

        var game = await new GameRepository(factory).AddAsync(new Game
        {
            Name = "Nie abgeglichen",
            ExecutablePath = @"C:\Games\X\game.exe",
            ExecutablePathKey = @"C:\GAMES\X\GAME.EXE",
            ExecutableName = "game.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        await new GameRepository(factory).DeleteAsync(game.Id, CancellationToken.None);

        await using var context = factory.CreateDbContext();

        // Im Konto hat dieses Spiel nie existiert - ein Grabstein wäre nur Ballast.
        Assert.AreEqual(0, await context.SyncTombstones.CountAsync());
    }

    [TestMethod]
    public async Task Deleting_a_synced_session_leaves_a_tombstone()
    {
        var (paths, factory, clock) = await CreateDatabaseAsync();
        using var _ = paths;

        var game = await AddSyncedGameAsync(factory, clock);

        long sessionId;
        await using (var context = factory.CreateDbContext())
        {
            var session = new GameSession
            {
                GameId = game.Id,
                StartedAtUtc = clock.UtcNow,
                LastSeenAtUtc = clock.UtcNow.AddHours(1),
                EndedAtUtc = clock.UtcNow.AddHours(1),
                DurationSeconds = 3600,
                BootSessionId = "202609152000",
                CloudIdentity = "ses:machine-a:game:manual:testspiel:1",
                SyncedHash = "hash"
            };
            context.GameSessions.Add(session);
            await context.SaveChangesAsync();
            sessionId = session.Id;
        }

        await new GameSessionRepository(factory).DeleteAsync(sessionId, CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var tombstone = await verify.SyncTombstones
            .FirstOrDefaultAsync(item => item.Kind == SyncEntityKind.Session);

        Assert.IsNotNull(tombstone);
        Assert.AreEqual("ses:machine-a:game:manual:testspiel:1", tombstone.Identity);
    }

    [TestMethod]
    public async Task Deleting_a_synced_exclusion_leaves_a_tombstone()
    {
        var (paths, factory, clock) = await CreateDatabaseAsync();
        using var _ = paths;

        var repository = new TrackingExclusionRepository(factory);
        var rule = await repository.AddAsync(new TrackingExclusionRule
        {
            Kind = TrackingExclusionKind.Directory,
            Value = @"C:\Games\Ignoriert",
            ValueKey = @"C:\GAMES\IGNORIERT",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        await using (var context = factory.CreateDbContext())
        {
            var stored = await context.TrackingExclusionRules.FirstAsync(item => item.Id == rule.Id);
            stored.CloudIdentity = "excl:directory:c:\\games\\ignoriert";
            stored.SyncedHash = "hash";
            await context.SaveChangesAsync();
        }

        await repository.DeleteAsync(rule.Id, CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        Assert.AreEqual(
            1,
            await verify.SyncTombstones.CountAsync(item => item.Kind == SyncEntityKind.Exclusion));
    }

    [TestMethod]
    public async Task Replacing_tags_tombstones_the_removed_ones()
    {
        var (paths, factory, clock) = await CreateDatabaseAsync();
        using var _ = paths;

        var game = await AddSyncedGameAsync(factory, clock);
        var repository = new GameRepository(factory);
        await repository.SetTagsAsync(game.Id, ["Rennspiel"], CancellationToken.None);

        await using (var context = factory.CreateDbContext())
        {
            var tag = await context.GameTags.FirstAsync(item => item.GameId == game.Id);
            tag.CloudIdentity = "tag:game:manual:testspiel:rennspiel";
            tag.SyncedHash = "hash";
            await context.SaveChangesAsync();
        }

        await repository.SetTagsAsync(game.Id, ["Simulation"], CancellationToken.None);

        await using var verify = factory.CreateDbContext();
        var tombstone = await verify.SyncTombstones.FirstOrDefaultAsync(item => item.Kind == SyncEntityKind.Tag);

        Assert.IsNotNull(tombstone);
        Assert.AreEqual("tag:game:manual:testspiel:rennspiel", tombstone.Identity);
    }
}
