using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameArtworkRepository
{
    Task<GameArtwork?> GetByGameIdAsync(long gameId, CancellationToken cancellationToken);

    Task UpsertAsync(GameArtwork artwork, CancellationToken cancellationToken);

    Task DeleteAsync(long gameId, CancellationToken cancellationToken);
}
