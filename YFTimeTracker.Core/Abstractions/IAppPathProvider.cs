namespace YFTimeTracker.Core.Abstractions;

public interface IAppPathProvider
{
    string DataDirectory { get; }

    string DatabasePath { get; }

    string LogDirectory { get; }

    string BackupDirectory { get; }

    string ExportDirectory { get; }
}
