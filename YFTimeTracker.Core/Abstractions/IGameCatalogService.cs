using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameCatalogService
{
    Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken);

    Task<Game> AddGameAsync(string executablePath, string? displayName, CancellationToken cancellationToken);

    Task UpdateGameAsync(long gameId, string displayName, string executablePath, CancellationToken cancellationToken);

    Task DeleteGameAsync(long gameId, CancellationToken cancellationToken);
}
