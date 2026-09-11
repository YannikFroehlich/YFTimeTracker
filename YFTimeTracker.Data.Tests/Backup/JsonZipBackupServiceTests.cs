using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;
using YFTimeTracker.Data.Backup;
using YFTimeTracker.Data.Repositories;
using TestRepositories = YFTimeTracker.Data.Tests.Repositories;

namespace YFTimeTracker.Data.Tests.Backup;

[TestClass]
public sealed class JsonZipBackupServiceTests
{
    [TestMethod]
    public async Task Version2_export_and_import_preserve_xbox_launcher_executables()
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
            Source = GameSource.Xbox,
            ExternalGameId = "Contoso.Neon_123",
            InstallDirectory = @"D:\XboxGames\Neon\Content",
            InstallDirectoryKey = @"D:\XBOXGAMES\NEON\CONTENT",
            ExecutablePath = @"D:\XboxGames\Neon\Content\game.exe",
            ExecutablePathKey = @"D:\XBOXGAMES\NEON\CONTENT\GAME.EXE",
            ExecutableName = "game.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);
        await repository.AddExecutableAsync(game.Id, new GameExecutable
        {
            ExecutablePath = @"D:\XboxGames\Neon\Content\bin\renderer.exe",
            ExecutablePathKey = @"D:\XBOXGAMES\NEON\CONTENT\BIN\RENDERER.EXE",
            ExecutableName = "renderer.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        var archivePath = Path.Combine(paths.ExportDirectory, "v2.zip");
        await backup.ExportAsync(archivePath, CancellationToken.None);
        await backup.ImportAsync(archivePath, CancellationToken.None);

        var imported = await repository.GetByExternalIdAsync(GameSource.Xbox, "Contoso.Neon_123", CancellationToken.None);
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

    [TestMethod]
    public async Task Version2_export_and_import_preserve_tags_and_pinned_state()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-11T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var repository = new GameRepository(factory);
        var game = await repository.AddAsync(new Game
        {
            Name = "Tagged Game",
            ExecutablePath = @"C:\Games\Tagged.exe",
            ExecutablePathKey = @"C:\GAMES\TAGGED.EXE",
            ExecutableName = "Tagged.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);
        await repository.SetPinnedAsync(game.Id, true, CancellationToken.None);
        await repository.SetTagsAsync(game.Id, ["Shooter", "Multiplayer"], CancellationToken.None);

        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        var archivePath = Path.Combine(paths.ExportDirectory, "v2-tags.zip");
        await backup.ExportAsync(archivePath, CancellationToken.None);
        await backup.ImportAsync(archivePath, CancellationToken.None);

        var imported = (await new GameRepository(factory).GetAllAsync(CancellationToken.None)).Single();
        Assert.IsTrue(imported.IsPinned);
        CollectionAssert.AreEquivalent(new[] { "Shooter", "Multiplayer" }, imported.Tags.Select(tag => tag.Tag).ToArray());
    }

    // Vor diesem Feature abgelegte Version-2-Archive haben keine "tags"-Eigenschaft im JSON - der
    // Import darf daran nicht scheitern (document.Tags ist dann einfach null).
    [TestMethod]
    public async Task Version2_archive_without_a_tags_property_imports_successfully()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-11T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var archivePath = Path.Combine(paths.ExportDirectory, "v2-no-tags.zip");
        var document = new
        {
            Manifest = new
            {
                AppName = "YFTimeTracker",
                ExportVersion = "2",
                CreatedAtUtc = clock.UtcNow,
                GameCount = 1,
                SessionCount = 0
            },
            Games = new[]
            {
                new
                {
                    Id = 1L,
                    Name = "Pre-tags Game",
                    Source = GameSource.Manual,
                    AddedAtUtc = clock.UtcNow,
                    LegacyExecutablePath = @"C:\Games\Old.exe",
                    LegacyExecutablePathKey = @"C:\GAMES\OLD.EXE",
                    LegacyExecutableName = "Old.exe"
                }
            },
            Executables = new[]
            {
                new
                {
                    Id = 1L,
                    GameId = 1L,
                    ExecutablePath = @"C:\Games\Old.exe",
                    ExecutablePathKey = @"C:\GAMES\OLD.EXE",
                    ExecutableName = "Old.exe",
                    IsPrimary = true,
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
        Assert.AreEqual("Pre-tags Game", imported.Name);
        Assert.IsFalse(imported.IsPinned);
        Assert.IsEmpty(imported.Tags);
    }

    [TestMethod]
    public async Task Prune_keeps_the_newest_backups_of_each_kind_beyond_the_retention_period()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        await settings.SetAsync(AppSettingKeys.BackupRetentionDays, "1", CancellationToken.None);

        // Alle Sicherungen sind deutlich älter als die Aufbewahrungsfrist von einem Tag.
        var dailyBackups = CreateAgedBackups(paths.BackupDirectory, "auto-", 4, clock.UtcNow.AddDays(-30));
        var safetyBackups = CreateAgedBackups(paths.BackupDirectory, "pre-migration-", 2, clock.UtcNow.AddDays(-40));

        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        await backup.PruneBackupsAsync(CancellationToken.None);

        var remaining = Directory.GetFiles(paths.BackupDirectory, "*.db")
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Von den täglichen Sicherungen bleiben die drei neuesten, die älteste fällt weg.
        Assert.HasCount(5, remaining);
        Assert.IsFalse(remaining.Contains(Path.GetFileName(dailyBackups[0])));
        foreach (var kept in dailyBackups.Skip(1).Concat(safetyBackups))
        {
            Assert.IsTrue(remaining.Contains(Path.GetFileName(kept)), $"{kept} sollte erhalten bleiben.");
        }
    }

    [TestMethod]
    public async Task Restore_brings_back_the_saved_state_and_keeps_a_safety_copy()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var repository = new GameRepository(factory);
        var game = await repository.AddAsync(new Game
        {
            Name = "Alpha",
            Source = GameSource.Manual,
            ExecutablePath = @"C:\Games\Alpha.exe",
            ExecutablePathKey = @"C:\GAMES\ALPHA.EXE",
            ExecutableName = "Alpha.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        var backupPath = await backup.CreateDailyBackupAsync(CancellationToken.None);
        Assert.IsNotNull(backupPath);

        await repository.DeleteAsync(game.Id, CancellationToken.None);
        Assert.IsEmpty(await repository.GetAllAsync(CancellationToken.None));

        var result = await backup.RestoreAsync(backupPath, CancellationToken.None);

        Assert.AreEqual("Alpha", (await repository.GetAllAsync(CancellationToken.None)).Single().Name);
        Assert.IsNotNull(result.SafetyBackupPath);
        Assert.IsTrue(File.Exists(result.SafetyBackupPath), "Vor dem Wiederherstellen fehlt die Sicherheitskopie.");
    }

    [TestMethod]
    public async Task Restore_rejects_an_unreadable_file_and_leaves_the_database_untouched()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var repository = new GameRepository(factory);
        await repository.AddAsync(new Game
        {
            Name = "Alpha",
            Source = GameSource.Manual,
            ExecutablePath = @"C:\Games\Alpha.exe",
            ExecutablePathKey = @"C:\GAMES\ALPHA.EXE",
            ExecutableName = "Alpha.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        var brokenBackup = Path.Combine(paths.BackupDirectory, "auto-20260101.db");
        await File.WriteAllTextAsync(brokenBackup, "Das ist keine SQLite-Datenbank.");

        var backup = new JsonZipBackupService(factory, paths, clock, settings);

        await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => backup.RestoreAsync(brokenBackup, CancellationToken.None));

        Assert.AreEqual("Alpha", (await repository.GetAllAsync(CancellationToken.None)).Single().Name);
    }

    [TestMethod]
    public async Task Daily_backup_is_not_mirrored_when_destination_is_local()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-10T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var externalFolder = Directory.CreateTempSubdirectory("YFTimeTracker.Tests.External").FullName;
        try
        {
            var backup = new JsonZipBackupService(factory, paths, clock, settings);
            await backup.CreateDailyBackupAsync(CancellationToken.None);

            Assert.IsEmpty(Directory.GetFileSystemEntries(externalFolder));
        }
        finally
        {
            Directory.Delete(externalFolder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Daily_backup_is_mirrored_to_a_valid_external_folder()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-10T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var externalFolder = Directory.CreateTempSubdirectory("YFTimeTracker.Tests.External").FullName;
        try
        {
            await settings.SetAsync(AppSettingKeys.BackupDestination, nameof(BackupDestinationKind.OneDrive), CancellationToken.None);
            await settings.SetAsync(AppSettingKeys.BackupExternalFolderPath, externalFolder, CancellationToken.None);

            var backup = new JsonZipBackupService(factory, paths, clock, settings);
            var backupPath = await backup.CreateDailyBackupAsync(CancellationToken.None);
            Assert.IsNotNull(backupPath);

            var mirroredPath = Path.Combine(externalFolder, "YFTimeTracker Backups", Path.GetFileName(backupPath));
            Assert.IsTrue(File.Exists(mirroredPath), "Die Sicherung wurde nicht in den externen Ordner gespiegelt.");
            Assert.AreEqual(new FileInfo(backupPath).Length, new FileInfo(mirroredPath).Length);
        }
        finally
        {
            Directory.Delete(externalFolder, recursive: true);
        }
    }

    [TestMethod]
    public async Task Daily_backup_succeeds_even_when_the_external_folder_is_unreachable()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-10T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        await settings.SetAsync(AppSettingKeys.BackupDestination, nameof(BackupDestinationKind.OneDrive), CancellationToken.None);
        await settings.SetAsync(
            AppSettingKeys.BackupExternalFolderPath,
            Path.Combine(paths.DataDirectory, "does-not-exist"),
            CancellationToken.None);

        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        var backupPath = await backup.CreateDailyBackupAsync(CancellationToken.None);

        Assert.IsNotNull(backupPath);
        Assert.IsTrue(File.Exists(backupPath));
    }

    [TestMethod]
    public async Task Daily_backup_is_not_mirrored_for_the_not_yet_implemented_yf_database_destination()
    {
        using var paths = new TestRepositories.TempAppPathProvider();
        var factory = new TestRepositories.TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var clock = new TestRepositories.TestClock(DateTimeOffset.Parse("2026-09-10T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var externalFolder = Directory.CreateTempSubdirectory("YFTimeTracker.Tests.External").FullName;
        try
        {
            await settings.SetAsync(AppSettingKeys.BackupDestination, nameof(BackupDestinationKind.YfDatabase), CancellationToken.None);
            await settings.SetAsync(AppSettingKeys.BackupExternalFolderPath, externalFolder, CancellationToken.None);

            var backup = new JsonZipBackupService(factory, paths, clock, settings);
            var backupPath = await backup.CreateDailyBackupAsync(CancellationToken.None);

            Assert.IsNotNull(backupPath);
            Assert.IsEmpty(Directory.GetFileSystemEntries(externalFolder));
        }
        finally
        {
            Directory.Delete(externalFolder, recursive: true);
        }
    }

    private static string[] CreateAgedBackups(string directory, string prefix, int count, DateTimeOffset oldestCreatedAt)
    {
        var paths = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(directory, $"{prefix}{index:00}.db");
            File.WriteAllText(path, "backup");
            File.SetCreationTimeUtc(path, oldestCreatedAt.AddHours(index).UtcDateTime);
            paths.Add(path);
        }

        return [.. paths];
    }
}
