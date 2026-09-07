using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class BackupListItemViewModel(BackupInfo backup)
{
    public string FilePath => backup.FilePath;

    public string FileName => backup.FileName;

    public bool IsSafetyCopy => backup.IsSafetyCopy;

    public string KindLabel => backup.IsSafetyCopy ? "VOR ÄNDERUNG" : "AUTOMATISCH";

    public string CreatedText =>
        $"{TimeZoneInfo.ConvertTime(backup.CreatedAtUtc, TimeZoneInfo.Local):dd.MM.yyyy HH:mm}";

    public string SizeText => backup.SizeBytes < 1024 * 1024
        ? $"{Math.Max(backup.SizeBytes, 0) / 1024d:0.#} KB"
        : $"{backup.SizeBytes / 1024d / 1024d:0.#} MB";

    public string Summary => $"{CreatedText} · {SizeText}";
}
