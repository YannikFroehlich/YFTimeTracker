namespace YFTimeTracker.Core.Abstractions;

public interface IGameIconService
{
    Task<string?> GetIconPathAsync(string? executablePath, CancellationToken cancellationToken);
}
