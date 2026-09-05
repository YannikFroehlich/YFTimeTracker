using Microsoft.Extensions.DependencyInjection;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Windows.GameIcons;
using YFTimeTracker.Windows.Processes;
using YFTimeTracker.Windows.SystemInfo;

namespace YFTimeTracker.Windows;

public static class WindowsServiceCollectionExtensions
{
    public static IServiceCollection AddYFTimeTrackerWindowsServices(this IServiceCollection services)
    {
        services.AddSingleton<IAppPathProvider, WindowsAppPathProvider>();
        services.AddSingleton<IProcessSnapshotProvider, WindowsProcessSnapshotProvider>();
        services.AddSingleton<IGameInstallationProvider, WindowsGameInstallationProvider>();
        services.AddSingleton<IGameIconService, WindowsGameIconService>();
        services.AddSingleton<IBootSessionProvider, WindowsBootSessionProvider>();
        services.AddSingleton<ISystemSuspendNotifier, WindowsSystemSuspendNotifier>();
        services.AddSingleton<IStartupService, UnavailableStartupService>();
        return services;
    }
}
