using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data;

public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed class SqliteDatabaseInitializer(
    IDbContextFactory<YFTimeTrackerDbContext> contextFactory,
    IAppPathProvider appPathProvider,
    IBackupService backupService,
    ISettingsStore settings,
    ILogger<SqliteDatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(appPathProvider.DataDirectory);
        Directory.CreateDirectory(appPathProvider.BackupDirectory);
        Directory.CreateDirectory(appPathProvider.ExportDirectory);
        Directory.CreateDirectory(appPathProvider.LogDirectory);
        var databaseExistedBeforeStartup = File.Exists(appPathProvider.DatabasePath);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        if (File.Exists(appPathProvider.DatabasePath) && pendingMigrations.Any())
        {
            var backup = await backupService.CreatePreMigrationBackupAsync(cancellationToken);
            logger.LogInformation("Created pre-migration backup at {BackupPath}.", backup);
        }

        await context.Database.MigrateAsync(cancellationToken);

        await SetDefaultIfMissingAsync(AppSettingKeys.TrackingEnabled, bool.TrueString, cancellationToken);
        await SetDefaultIfMissingAsync(AppSettingKeys.LauncherDiscoveryEnabled, bool.TrueString, cancellationToken);
        await SetDefaultIfMissingAsync(AppSettingKeys.TrackingIntervalSeconds, "3", cancellationToken);
        await SetDefaultIfMissingAsync(AppSettingKeys.HeartbeatIntervalSeconds, "30", cancellationToken);
        await SetDefaultIfMissingAsync(AppSettingKeys.BackupRetentionDays, "14", cancellationToken);
        await SetDefaultIfMissingAsync(AppSettingKeys.MinimizeOnClose, bool.TrueString, cancellationToken);
        await SetDefaultIfMissingAsync(
            AppSettingKeys.FirstRunSetupCompleted,
            databaseExistedBeforeStartup.ToString(),
            cancellationToken);

        await backupService.CreateDailyBackupAsync(cancellationToken);
        await backupService.PruneBackupsAsync(cancellationToken);
    }

    private async Task SetDefaultIfMissingAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (await settings.GetAsync(key, cancellationToken) is null)
        {
            await settings.SetAsync(key, value, cancellationToken);
        }
    }
}
