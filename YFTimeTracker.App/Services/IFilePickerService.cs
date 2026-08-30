namespace YFTimeTracker.App.Services;

public interface IFilePickerService
{
    Task<string?> PickExecutableAsync(CancellationToken cancellationToken);

    Task<string?> PickExportArchiveAsync(CancellationToken cancellationToken);

    Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken);
}
