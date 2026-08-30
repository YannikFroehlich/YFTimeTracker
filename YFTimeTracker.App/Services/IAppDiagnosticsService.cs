namespace YFTimeTracker.App.Services;

public interface IAppDiagnosticsService
{
    AppDiagnosticsSnapshot GetSnapshot();

    void OpenLogDirectory();

    Task<AppDiagnosticsExportResult> ExportAsync(string archivePath, CancellationToken cancellationToken);
}
