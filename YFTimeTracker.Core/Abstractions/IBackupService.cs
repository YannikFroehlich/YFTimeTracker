using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IBackupService
{
    Task<string?> CreateDailyBackupAsync(CancellationToken cancellationToken);

    Task<string?> CreatePreMigrationBackupAsync(CancellationToken cancellationToken);

    Task PruneBackupsAsync(CancellationToken cancellationToken);

    IReadOnlyList<BackupInfo> GetBackups();

    Task<RestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken);

    Task<ExportResult> ExportAsync(string archivePath, CancellationToken cancellationToken);

    Task<ImportResult> ImportAsync(string archivePath, CancellationToken cancellationToken);
}
