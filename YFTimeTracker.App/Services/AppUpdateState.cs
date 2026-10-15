namespace YFTimeTracker.App.Services;

public enum AppUpdateStage
{
    Idle,
    Unavailable,
    Checking,
    UpToDate,
    Available,
    Downloading,
    ReadyToInstall,
    Applying,
    Failed
}

public sealed record AppUpdateState(
    AppUpdateStage Stage,
    string CurrentVersion,
    string Message,
    string? AvailableVersion = null,
    long DownloadSize = 0,
    int DownloadProgress = 0,
    string? ReleaseNotes = null)
{
    public bool IsBusy => Stage is AppUpdateStage.Checking or AppUpdateStage.Downloading or AppUpdateStage.Applying;

    public bool CanCheckForUpdates => Stage != AppUpdateStage.Unavailable && !IsBusy;

    public bool HasAvailableUpdate => Stage is AppUpdateStage.Available or AppUpdateStage.ReadyToInstall;

    /// <summary>
    /// Downloadgröße wie "12,3 MB"; leer, solange sie unbekannt ist.
    /// </summary>
    public string DownloadSizeText => DownloadSize <= 0
        ? string.Empty
        : DownloadSize >= 1024L * 1024L
            ? $"{DownloadSize / (1024d * 1024d):0.#} MB"
            : $"{DownloadSize / 1024d:0.#} KB";
}
