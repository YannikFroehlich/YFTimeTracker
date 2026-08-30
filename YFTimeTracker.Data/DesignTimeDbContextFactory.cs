using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YFTimeTracker.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<YFTimeTrackerDbContext>
{
    public YFTimeTrackerDbContext CreateDbContext(string[] args)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDirectory = Path.Combine(appData, "YFTimeTracker");
        Directory.CreateDirectory(dataDirectory);

        var options = new DbContextOptionsBuilder<YFTimeTrackerDbContext>()
            .UseSqlite($"Data Source={Path.Combine(dataDirectory, "yftimetracker.db")}")
            .Options;

        return new YFTimeTrackerDbContext(options);
    }
}
