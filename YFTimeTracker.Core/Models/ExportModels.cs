namespace YFTimeTracker.Core.Models;

public sealed record ExportResult(string ArchivePath, int GameCount, int SessionCount);

public sealed record ImportResult(string ArchivePath, int GameCount, int SessionCount, bool DatabaseReplaced);

public sealed record BackupInfo(
    string FilePath,
    string FileName,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    bool IsSafetyCopy);

public sealed record RestoreResult(string BackupPath, string? SafetyBackupPath);
