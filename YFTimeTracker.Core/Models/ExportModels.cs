namespace YFTimeTracker.Core.Models;

public sealed record ExportResult(string ArchivePath, int GameCount, int SessionCount);

public sealed record ImportResult(string ArchivePath, int GameCount, int SessionCount, bool DatabaseReplaced);
