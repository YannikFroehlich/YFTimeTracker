namespace YFTimeTracker.App.Services;

public sealed record AppDiagnosticsSnapshot(
    string Version,
    string Distribution,
    string InstallDirectory,
    string DataDirectory,
    string LogDirectory,
    string RuntimeDescription,
    string OperatingSystemDescription,
    string ProcessArchitecture);

public sealed record AppDiagnosticsExportResult(string ArchivePath, int IncludedLogCount);
