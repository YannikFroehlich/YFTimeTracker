using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Repositories;

namespace YFTimeTracker.Data.Tests.Repositories;

[TestClass]
public sealed class ArtworkAndExclusionRepositoryTests
{
    [TestMethod]
    public async Task Migration_adds_artwork_and_exclusion_tables_without_changing_existing_games()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260911052653_AddGameFavoritesAndTags");
            var addedAtUtc = DateTimeOffset.Parse("2026-09-11T10:00:00Z").UtcTicks;
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO Games (Name, ExecutablePath, ExecutablePathKey, ExecutableName, Source, AddedAtUtc, IsPinned) VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
                "Existing Game", @"C:\Games\Existing.exe", @"C:\GAMES\EXISTING.EXE", "Existing.exe", 0, addedAtUtc, false);

            await migrator.MigrateAsync();
        }

        var existing = (await new GameRepository(factory).GetAllAsync(CancellationToken.None)).Single();
        Assert.AreEqual("Existing Game", existing.Name);
        Assert.IsNull(await new GameArtworkRepository(factory).GetByGameIdAsync(existing.Id, CancellationToken.None));
        Assert.IsEmpty(await new TrackingExclusionRepository(factory).GetAllAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Artwork_is_upserted_and_deleted_with_its_game()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var games = new GameRepository(factory);
        var game = await games.AddAsync(new Game
        {
            Name = "Cover Game",
            ExecutablePath = @"C:\Games\Cover\game.exe",
            ExecutablePathKey = @"C:\GAMES\COVER\GAME.EXE",
            ExecutableName = "game.exe",
            AddedAtUtc = DateTimeOffset.Parse("2026-09-13T10:00:00Z")
        }, CancellationToken.None);
        var artworks = new GameArtworkRepository(factory);

        await artworks.UpsertAsync(CreateArtwork(game.Id, [1, 2, 3]), CancellationToken.None);
        await artworks.UpsertAsync(CreateArtwork(game.Id, [4, 5, 6, 7]), CancellationToken.None);

        var stored = await artworks.GetByGameIdAsync(game.Id, CancellationToken.None);
        Assert.IsNotNull(stored);
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7 }, stored.ImageData);

        await games.DeleteAsync(game.Id, CancellationToken.None);
        Assert.IsNull(await artworks.GetByGameIdAsync(game.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task Tracking_exclusions_are_persisted_and_removed()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var repository = new TrackingExclusionRepository(factory);
        var added = await repository.AddAsync(new TrackingExclusionRule
        {
            Kind = TrackingExclusionKind.Directory,
            Value = @"C:\Games\Ignored",
            ValueKey = @"C:\GAMES\IGNORED",
            AddedAtUtc = DateTimeOffset.Parse("2026-09-13T10:00:00Z")
        }, CancellationToken.None);

        var stored = (await repository.GetAllAsync(CancellationToken.None)).Single();
        Assert.AreEqual(added.Id, stored.Id);
        Assert.AreEqual(TrackingExclusionKind.Directory, stored.Kind);

        await repository.DeleteAsync(added.Id, CancellationToken.None);
        Assert.IsEmpty(await repository.GetAllAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Merge_keeps_the_source_cover_when_the_target_has_none()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var games = new GameRepository(factory);
        var source = await AddGameAsync(games, "Source", @"C:\Games\Source.exe");
        var target = await AddGameAsync(games, "Target", @"C:\Games\Target.exe");
        var artworks = new GameArtworkRepository(factory);
        await artworks.UpsertAsync(CreateArtwork(source.Id, [1, 2, 3]), CancellationToken.None);

        await games.MergeIntoAsync(
            source.Id,
            target.Id,
            YFTimeTracker.Core.Services.SessionMergePlanner.Create([], []),
            CancellationToken.None);

        Assert.IsNull(await artworks.GetByGameIdAsync(source.Id, CancellationToken.None));
        Assert.IsNotNull(await artworks.GetByGameIdAsync(target.Id, CancellationToken.None));
    }

    private static GameArtwork CreateArtwork(long gameId, byte[] data) => new()
    {
        GameId = gameId,
        ContentType = "image/png",
        FileExtension = ".png",
        Sha256 = new string('A', 64),
        ImageData = data,
        UpdatedAtUtc = DateTimeOffset.Parse("2026-09-13T10:00:00Z")
    };

    private static Task<Game> AddGameAsync(GameRepository repository, string name, string executablePath)
    {
        return repository.AddAsync(new Game
        {
            Name = name,
            ExecutablePath = executablePath,
            ExecutablePathKey = executablePath.ToUpperInvariant(),
            ExecutableName = Path.GetFileName(executablePath),
            AddedAtUtc = DateTimeOffset.Parse("2026-09-13T10:00:00Z")
        }, CancellationToken.None);
    }
}
