namespace YFTimeTracker.Core.Abstractions;

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, string value, CancellationToken cancellationToken);

    Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken);

    Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken);
}
