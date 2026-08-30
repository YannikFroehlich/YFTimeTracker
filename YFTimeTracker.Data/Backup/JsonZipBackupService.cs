using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Data.Backup;

public sealed class JsonZipBackupService(
    IDbContextFactory<YFTimeTrackerDbContext> contextFactory,
    IAppPathProvider appPathProvider,
    IClock clock,
    ISettingsStore settingsStore) : IBackupService
{
    private const string ExportVersion = "2";
    private const string DataEntryName = "yftimetracker-data.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<string?> CreateDailyBackupAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appPathProvider.DatabasePath))
        {
            return null;
        }

        Directory.CreateDirectory(appPathProvider.BackupDirectory);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var backupPath = Path.Combine(appPathProvider.BackupDirectory, $"auto-{today:yyyyMMdd}.db");
        if (File.Exists(backupPath))
        {
            return null;
        }

        SqliteConnection.ClearAllPools();
        File.Copy(appPathProvider.DatabasePath, backupPath, overwrite: false);
        await settingsStore.SetAsync(AppSettingKeys.LastBackupDate, today.ToString("O"), cancellationToken);
        return backupPath;
    }

    public Task<string?> CreatePreMigrationBackupAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(appPathProvider.DatabasePath))
        {
            return Task.FromResult<string?>(null);
        }

        Directory.CreateDirectory(appPathProvider.BackupDirectory);
        var backupPath = Path.Combine(appPathProvider.BackupDirectory, $"pre-migration-{clock.UtcNow:yyyyMMddHHmmss}.db");

        SqliteConnection.ClearAllPools();
        File.Copy(appPathProvider.DatabasePath, backupPath, overwrite: false);
        return Task.FromResult<string?>(backupPath);
    }

    public async Task PruneBackupsAsync(CancellationToken cancellationToken)
    {
        var retentionDays = await settingsStore.GetIntAsync(AppSettingKeys.BackupRetentionDays, 14, cancellationToken);
        var cutoff = clock.UtcNow.AddDays(-Math.Max(retentionDays, 1));

        if (!Directory.Exists(appPathProvider.BackupDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(appPathProvider.BackupDirectory, "*.db"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var created = File.GetCreationTimeUtc(file);
            if (created < cutoff.UtcDateTime)
            {
                File.Delete(file);
            }
        }
    }

    public async Task<ExportResult> ExportAsync(string archivePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? appPathProvider.ExportDirectory);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var games = await context.Games.AsNoTracking().OrderBy(game => game.Id).ToListAsync(cancellationToken);
        var executables = await context.GameExecutables.AsNoTracking().OrderBy(executable => executable.Id).ToListAsync(cancellationToken);
        var sessions = await context.GameSessions.AsNoTracking().OrderBy(session => session.Id).ToListAsync(cancellationToken);
        var settings = await context.AppSettings.AsNoTracking().OrderBy(setting => setting.Key).ToListAsync(cancellationToken);

        var document = new BackupDocument(
            new BackupManifest("YFTimeTracker", ExportVersion, clock.UtcNow, games.Count, sessions.Count),
            games,
            executables,
            sessions,
            settings);

        await using var fileStream = File.Create(archivePath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);
        var dataEntry = archive.CreateEntry(DataEntryName, CompressionLevel.Optimal);
        await using var entryStream = dataEntry.Open();
        await JsonSerializer.SerializeAsync(entryStream, document, JsonOptions, cancellationToken);

        return new ExportResult(archivePath, games.Count, sessions.Count);
    }

    public async Task<ImportResult> ImportAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(archivePath))
        {
            throw new YFTimeTrackerException("Die Import-Datei wurde nicht gefunden.");
        }

        BackupDocument document;
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var dataEntry = archive.GetEntry(DataEntryName)
                ?? throw new YFTimeTrackerException("Das Archiv enthaelt keine YFTimeTracker-Daten.");

            await using var entryStream = dataEntry.Open();
            using var json = await JsonDocument.ParseAsync(entryStream, cancellationToken: cancellationToken);
            var version = json.RootElement.GetProperty("manifest").GetProperty("exportVersion").GetString();
            document = version switch
            {
                "1" => UpgradeLegacyBackup(json.RootElement),
                "2" => json.RootElement.Deserialize<BackupDocument>(JsonOptions)
                    ?? throw new YFTimeTrackerException("Das Archiv konnte nicht gelesen werden."),
                _ => throw new YFTimeTrackerException("Diese Export-Version wird nicht unterstützt.")
            };
        }

        ValidateBackup(document);

        Directory.CreateDirectory(appPathProvider.DataDirectory);
        var tempDatabase = Path.Combine(appPathProvider.DataDirectory, $"import-{Guid.NewGuid():N}.db");

        try
        {
            var tempOptions = new DbContextOptionsBuilder<YFTimeTrackerDbContext>()
                .UseSqlite($"Data Source={tempDatabase}")
                .Options;

            await using (var tempContext = new YFTimeTrackerDbContext(tempOptions))
            {
                await tempContext.Database.MigrateAsync(cancellationToken);
                foreach (var game in document.Games)
                {
                    var primary = document.Executables.Single(executable => executable.GameId == game.Id && executable.IsPrimary);
                    game.LegacyExecutablePath = primary.ExecutablePath;
                    game.LegacyExecutablePathKey = primary.ExecutablePathKey;
                    game.LegacyExecutableName = primary.ExecutableName;
                }
                tempContext.Games.AddRange(document.Games);
                tempContext.GameExecutables.AddRange(document.Executables);
                tempContext.GameSessions.AddRange(document.Sessions);
                tempContext.AppSettings.AddRange(document.Settings);
                await tempContext.SaveChangesAsync(cancellationToken);
            }

            await CreatePreMigrationBackupAsync(cancellationToken);
            SqliteConnection.ClearAllPools();
            File.Move(tempDatabase, appPathProvider.DatabasePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempDatabase))
            {
                File.Delete(tempDatabase);
            }
        }

        return new ImportResult(archivePath, document.Games.Count, document.Sessions.Count, DatabaseReplaced: true);
    }

    private static void ValidateBackup(BackupDocument document)
    {
        if (!string.Equals(document.Manifest.AppName, "YFTimeTracker", StringComparison.Ordinal))
        {
            throw new YFTimeTrackerException("Das Archiv gehoert nicht zu YFTimeTracker.");
        }

        if (!string.Equals(document.Manifest.ExportVersion, ExportVersion, StringComparison.Ordinal))
        {
            throw new YFTimeTrackerException("Diese Export-Version wird nicht unterstuetzt.");
        }

        var gameIds = document.Games.Select(game => game.Id).ToHashSet();
        var externalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in document.Games)
        {
            if (string.IsNullOrWhiteSpace(game.Name) ||
                (game.ExternalGameId is not null && !externalIds.Add($"{game.Source}:{game.ExternalGameId}")))
            {
                throw new YFTimeTrackerException("Das Archiv enthaelt ungueltige oder doppelte Spiele.");
            }
        }

        var pathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var primaryGameIds = new HashSet<long>();
        foreach (var executable in document.Executables)
        {
            if (!gameIds.Contains(executable.GameId) ||
                string.IsNullOrWhiteSpace(executable.ExecutablePath) ||
                string.IsNullOrWhiteSpace(executable.ExecutablePathKey) ||
                !pathKeys.Add(executable.ExecutablePathKey) ||
                (executable.IsPrimary && !primaryGameIds.Add(executable.GameId)))
            {
                throw new YFTimeTrackerException("Das Archiv enthält ungültige oder doppelte EXE-Zuordnungen.");
            }
        }

        if (gameIds.Any(gameId => !primaryGameIds.Contains(gameId)))
        {
            throw new YFTimeTrackerException("Mindestens einem Spiel fehlt die primäre EXE-Zuordnung.");
        }

        var openSessionGameIds = new HashSet<long>();
        foreach (var session in document.Sessions)
        {
            if (!gameIds.Contains(session.GameId) ||
                session.LastSeenAtUtc < session.StartedAtUtc ||
                session.EndedAtUtc < session.StartedAtUtc ||
                (session.EndedAtUtc is null && !openSessionGameIds.Add(session.GameId)))
            {
                throw new YFTimeTrackerException("Das Archiv enthaelt ungueltige Sessions.");
            }
        }
    }

    private static BackupDocument UpgradeLegacyBackup(JsonElement root)
    {
        var legacy = root.Deserialize<LegacyBackupDocument>(JsonOptions)
            ?? throw new YFTimeTrackerException("Das Version-1-Archiv konnte nicht gelesen werden.");

        var games = legacy.Games.Select(game => new Game
        {
            Id = game.Id,
            Name = game.Name,
            Source = GameSource.Manual,
            AddedAtUtc = game.AddedAtUtc
        }).ToArray();

        var executables = legacy.Games.Select((game, index) => new GameExecutable
        {
            Id = index + 1,
            GameId = game.Id,
            ExecutablePath = game.ExecutablePath,
            ExecutablePathKey = game.ExecutablePathKey,
            ExecutableName = game.ExecutableName,
            IsPrimary = true,
            AddedAtUtc = game.AddedAtUtc
        }).ToArray();

        return new BackupDocument(
            legacy.Manifest with { ExportVersion = ExportVersion },
            games,
            executables,
            legacy.Sessions,
            legacy.Settings);
    }
}
