using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameInstallationProvider
{
    Task<LauncherDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}
