namespace YFTimeTracker.Core.Abstractions;

public enum StartupState
{
    Unavailable,
    Disabled,
    Enabled,
    DisabledByPolicy
}

public interface IStartupService
{
    Task<StartupState> GetStateAsync(CancellationToken cancellationToken);

    Task<StartupState> SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}
