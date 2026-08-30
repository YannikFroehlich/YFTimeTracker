using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Backup;
using YFTimeTracker.Data.Repositories;
using TestRepositories = YFTimeTracker.Data.Tests.Repositories;

namespace YFTimeTracker.Data.Tests.Backup;

[TestClass]
public sealed class JsonZipBackupServiceTests
{
    [TestMethod]
    public async Task Version2_export_and_import_preserve_launcher_executables()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var repository = new GameRepository(factory);
        var game = await repository.AddAsync(new Game
        {
            Name = "Neon Game",
            Source = GameSource.Epic,
            ExternalGameId = "catalog-1",
            InstallDirectory = @"D:\Epic\Neon",
            InstallDirectoryKey = @"D:\EPIC\NEON",
            ExecutablePath = @"D:\Epic\Neon\game.exe",
            ExecutablePathKey = @"D:\EPIC\NEON\GAME.EXE",
            ExecutableName = "game.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);
        await repository.AddExecutableAsync(game.Id, new GameExecutable
        {
            ExecutablePath = @"D:\Epic\Neon\bin\renderer.exe",
            ExecutablePathKey = @"D:\EPIC\NEON\BIN\RENDERER.EXE",
            ExecutableName = "renderer.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        var archivePath = Path.Combine(paths.ExportDirectory, "v2.zip");
        await backup.ExportAsync(archivePath, CancellationToken.None);
        await backup.ImportAsync(archivePath, CancellationToken.None);

        var imported = await repository.GetByExternalIdAsync(GameSource.Epic, "catalog-1", CancellationToken.None);
        Assert.IsNotNull(imported);
        Assert.HasCount(2, imported.Executables);
        Assert.HasCount(1, imported.Executables.Where(executable => executable.IsPrimary));
    }

    [TestMethod]
    public async Task Version1_import_creates_manual_game_with_primary_executable()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var archivePath = Path.Combine(paths.ExportDirectory, "v1.zip");
        var document = new
        {
            Manifest = new
            {
                AppName = "YFTimeTracker",
                ExportVersion = "1",
                CreatedAtUtc = clock.UtcNow,
                GameCount = 1,
                SessionCount = 0
            },
            Games = new[]
            {
                new
                {
                    Id = 1L,
                    Name = "Legacy Game",
                    ExecutablePath = @"C:\Games\Legacy.exe",
                    ExecutablePathKey = @"C:\GAMES\LEGACY.EXE",
                    ExecutableName = "Legacy.exe",
                    AddedAtUtc = clock.UtcNow
                }
            },
            Sessions = Array.Empty<object>(),
            Settings = Array.Empty<object>()
        };
        await using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("yftimetracker-data.json");
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        await backup.ImportAsync(archivePath, CancellationToken.None);

        var imported = (await new GameRepository(factory).GetAllAsync(CancellationToken.None)).Single();
        Assert.AreEqual(GameSource.Manual, imported.Source);
        Assert.AreEqual(@"C:\GAMES\LEGACY.EXE", imported.PrimaryExecutable?.ExecutablePathKey);
    }
}
