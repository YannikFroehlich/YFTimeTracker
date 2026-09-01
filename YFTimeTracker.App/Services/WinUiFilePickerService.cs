using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.App.Services;

public sealed class WinUiFilePickerService(IAppPathProvider paths) : IFilePickerService
{
    public async Task<string?> PickExecutableAsync(CancellationToken cancellationToken)
    {
        var picker = new FileOpenPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    public async Task<string?> PickExportArchiveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.ExportDirectory);

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"YFTimeTracker-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        InitializePicker(picker);
        picker.FileTypeChoices.Add("YFTimeTracker Export", [".zip"]);

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    public async Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        InitializePicker(picker);
        picker.FileTypeFilter.Add(".zip");

        var file = await picker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    public async Task<string?> PickDiagnosticsArchiveAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.ExportDirectory);

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"YFTimeTracker-Diagnose-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        InitializePicker(picker);
        picker.FileTypeChoices.Add("YFTimeTracker Diagnosebericht", [".zip"]);

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    public async Task<string?> PickYearReviewImageAsync(int year, CancellationToken cancellationToken)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"YFTimeTracker-Jahresrueckblick-{year}"
        };
        InitializePicker(picker);
        picker.FileTypeChoices.Add("PNG-Bild", [".png"]);

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    public async Task<string?> PickStatisticsExportAsync(string periodLabel, CancellationToken cancellationToken)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"YFTimeTracker-Statistik-{periodLabel}-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        InitializePicker(picker);
        picker.FileTypeChoices.Add("CSV-Datei", [".csv"]);

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    public async Task<string?> PickSessionsExportAsync(string periodLabel, CancellationToken cancellationToken)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"YFTimeTracker-Sessions-{periodLabel}-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        InitializePicker(picker);
        picker.FileTypeChoices.Add("CSV-Datei", [".csv"]);

        var file = await picker.PickSaveFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path;
    }

    private static void InitializePicker(object picker)
    {
        var window = App.MainWindow ?? throw new InvalidOperationException("No main window is available.");
        var handle = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, handle);
    }
}
