using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Backup;
using YFTimeTracker.Data.Repositories;
using YFTimeTracker.Data.Tests.Repositories;

namespace YFTimeTracker.Data.Tests.Initialization;

[TestClass]
public sealed class SqliteDatabaseInitializerTests
{
    [TestMethod]
    public async Task New_database_requires_first_run_setup()
    {
        using var paths = new TempAppPathProvider();
        var settings = await InitializeAsync(paths);

        Assert.IsFalse(await settings.GetBoolAsync(
            AppSettingKeys.FirstRunSetupCompleted,
            true,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task Existing_database_skips_first_run_setup_after_update()
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
        }

        var settings = await InitializeAsync(paths);

        Assert.IsTrue(await settings.GetBoolAsync(
            AppSettingKeys.FirstRunSetupCompleted,
            false,
            CancellationToken.None));
    }

    private static async Task<SettingsStore> InitializeAsync(TempAppPathProvider paths)
    {
        var factory = new TestDbContextFactory(paths.DatabasePath);
        var clock = new TestClock(DateTimeOffset.Parse("2026-08-30T12:00:00Z"));
        var settings = new SettingsStore(factory, clock);
        var backup = new JsonZipBackupService(factory, paths, clock, settings);
        var initializer = new SqliteDatabaseInitializer(
            factory,
            paths,
            backup,
            settings,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);
        return settings;
    }
}
