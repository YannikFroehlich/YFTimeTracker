namespace YFTimeTracker.Core.Abstractions;

public interface IGameIconService
{
    Task<string?> GetIconPathAsync(string? executablePath, CancellationToken cancellationToken);

    Task<string?> GetGameImagePathAsync(
        long gameId,
        string? executablePath,
        CancellationToken cancellationToken)
    {
        return GetIconPathAsync(executablePath, cancellationToken);
    }

    Task<bool> HasCustomCoverAsync(long gameId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    Task<string> SetCustomCoverAsync(
        long gameId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Eigene Cover werden von diesem Bilddienst nicht unterstützt.");
    }

    Task RemoveCustomCoverAsync(long gameId, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
