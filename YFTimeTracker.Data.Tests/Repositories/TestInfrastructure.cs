using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Data.Tests.Repositories;

internal sealed class TestDbContextFactory(string databasePath) : IDbContextFactory<YFTimeTrackerDbContext>
{
    public YFTimeTrackerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<YFTimeTrackerDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        return new YFTimeTrackerDbContext(options);
    }

    public Task<YFTimeTrackerDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}

internal sealed class TempAppPathProvider : IAppPathProvider, IDisposable
{
    public TempAppPathProvider()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "YFTimeTracker.Tests", Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(DataDirectory, "yftimetracker.db");
        LogDirectory = Path.Combine(DataDirectory, "Logs");
        BackupDirectory = Path.Combine(DataDirectory, "Backups");
        ExportDirectory = Path.Combine(DataDirectory, "Exports");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(BackupDirectory);
        Directory.CreateDirectory(ExportDirectory);
    }

    public string DataDirectory { get; }

    public string DatabasePath { get; }

    public string LogDirectory { get; }

    public string BackupDirectory { get; }

    public string ExportDirectory { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(DataDirectory))
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(DataDirectory, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(100);
                    SqliteConnection.ClearAllPools();
                }
            }
        }
    }
}

internal sealed class TestClock(DateTimeOffset nowUtc) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = nowUtc;
}
