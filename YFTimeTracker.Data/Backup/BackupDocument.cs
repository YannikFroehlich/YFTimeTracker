using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Backup;

internal sealed record BackupDocument(
    BackupManifest Manifest,
    IReadOnlyList<Game> Games,
    IReadOnlyList<GameExecutable> Executables,
    IReadOnlyList<GameSession> Sessions,
    IReadOnlyList<AppSetting> Settings);

internal sealed record LegacyBackupDocument(
    BackupManifest Manifest,
    IReadOnlyList<LegacyGame> Games,
    IReadOnlyList<GameSession> Sessions,
    IReadOnlyList<AppSetting> Settings);

internal sealed record LegacyGame(
    long Id,
    string Name,
    string ExecutablePath,
    string ExecutablePathKey,
    string ExecutableName,
    DateTimeOffset AddedAtUtc);

internal sealed record BackupManifest(
    string AppName,
    string ExportVersion,
    DateTimeOffset CreatedAtUtc,
    int GameCount,
    int SessionCount);
