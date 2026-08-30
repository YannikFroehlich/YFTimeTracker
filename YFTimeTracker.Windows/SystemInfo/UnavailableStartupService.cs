using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.SystemInfo;

public sealed class UnavailableStartupService : IStartupService
{
    public Task<StartupState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StartupState.Unavailable);
    }

    public Task<StartupState> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(StartupState.Unavailable);
    }
}
