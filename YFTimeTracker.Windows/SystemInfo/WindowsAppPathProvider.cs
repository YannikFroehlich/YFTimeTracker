using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.SystemInfo;

public sealed class WindowsAppPathProvider : IAppPathProvider
{
    private readonly Lazy<string> dataDirectory = new(() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YFTimeTracker"));

    public string DataDirectory => dataDirectory.Value;

    public string DatabasePath => Path.Combine(DataDirectory, "yftimetracker.db");

    public string LogDirectory => Path.Combine(DataDirectory, "Logs");

    public string BackupDirectory => Path.Combine(DataDirectory, "Backups");

    public string ExportDirectory => Path.Combine(DataDirectory, "Exports");
}
