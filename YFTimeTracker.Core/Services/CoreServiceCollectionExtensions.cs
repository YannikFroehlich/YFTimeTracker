using Microsoft.Extensions.DependencyInjection;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Core.Services;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddYFTimeTrackerCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IGameCatalogService, GameCatalogService>();
        services.AddSingleton<IGameSessionEditor, GameSessionEditor>();
        services.AddSingleton<IPlaytimeStatisticsService, PlaytimeStatisticsService>();
        services.AddSingleton<IGameTrackingService, GameTrackingService>();
        return services;
    }
}
