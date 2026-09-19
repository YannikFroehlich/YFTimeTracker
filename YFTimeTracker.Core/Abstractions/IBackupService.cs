using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IBackupService
{
    Task<string?> CreateDailyBackupAsync(CancellationToken cancellationToken);

    Task<string?> CreatePreMigrationBackupAsync(CancellationToken cancellationToken);

    Task PruneBackupsAsync(CancellationToken cancellationToken);

    /// <summary>Wahr, wenn die neueste Tagessicherung danach im Konto liegt. Wirft nie.</summary>
    Task<bool> MirrorDailyBackupToCloudAsync(CancellationToken cancellationToken);

    /// <summary>Holt im Konto liegende Tagessicherungen, die lokal fehlen. Gibt die Anzahl zurück.</summary>
    Task<int> DownloadCloudBackupsAsync(CancellationToken cancellationToken);

    IReadOnlyList<BackupInfo> GetBackups();

    Task<RestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken);

    Task<ExportResult> ExportAsync(string archivePath, CancellationToken cancellationToken);

    Task<ImportResult> ImportAsync(string archivePath, CancellationToken cancellationToken);
}
