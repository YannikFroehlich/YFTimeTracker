namespace YFTimeTracker.App.Services;

public interface IFilePickerService
{
    Task<string?> PickExecutableAsync(CancellationToken cancellationToken);

    Task<string?> PickExportArchiveAsync(CancellationToken cancellationToken);

    Task<string?> PickDiagnosticsArchiveAsync(CancellationToken cancellationToken);

    Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken);

    Task<string?> PickYearReviewImageAsync(int year, CancellationToken cancellationToken);

    Task<string?> PickStatisticsExportAsync(string periodLabel, CancellationToken cancellationToken);

    Task<string?> PickSessionsExportAsync(string periodLabel, CancellationToken cancellationToken);
}
