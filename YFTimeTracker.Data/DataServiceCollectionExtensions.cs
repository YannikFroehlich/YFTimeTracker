using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Data.Backup;
using YFTimeTracker.Data.Repositories;

namespace YFTimeTracker.Data;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddYFTimeTrackerData(this IServiceCollection services)
    {
        services.AddDbContextFactory<YFTimeTrackerDbContext>((provider, options) =>
        {
            var paths = provider.GetRequiredService<IAppPathProvider>();
            options.UseSqlite($"Data Source={paths.DatabasePath}");
        });

        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IGameRepository, GameRepository>();
        services.AddSingleton<IGameSessionRepository, GameSessionRepository>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IBackupService, JsonZipBackupService>();
        return services;
    }
}
