using Microsoft.Extensions.DependencyInjection;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Cloud;

public static class CloudServiceCollectionExtensions
{
    public static IServiceCollection AddYFTimeTrackerCloud(this IServiceCollection services)
    {
        // Ein einzelner, langlebiger HttpClient reicht: die App spricht genau ein
        // Supabase-Projekt an. Das Timeout verhindert, dass ein haengender Abgleich
        // den Hintergrundlauf unbegrenzt blockiert.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromMinutes(2) });

        services.AddSingleton<ICloudConnectionProvider, CloudConfigConnectionProvider>();
        services.AddSingleton<ICloudAuthService, SupabaseAuthService>();
        services.AddSingleton<SupabaseAccountClient>();
        services.AddSingleton<IAccountSyncClient>(provider => provider.GetRequiredService<SupabaseAccountClient>());
        services.AddSingleton<ICloudBackupClient>(provider => provider.GetRequiredService<SupabaseAccountClient>());
        services.AddSingleton<IAccountSyncService, AccountSyncService>();
        services.AddSingleton<SessionSyncTrigger>();
        return services;
    }
}
