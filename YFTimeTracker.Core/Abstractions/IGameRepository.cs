using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameRepository
{
    Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken);

    Task<Game?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<Game?> GetByExecutablePathKeyAsync(string executablePathKey, CancellationToken cancellationToken);

    Task<Game?> GetByExternalIdAsync(GameSource source, string externalGameId, CancellationToken cancellationToken);

    Task<Game> AddAsync(Game game, CancellationToken cancellationToken);

    Task UpdateAsync(Game game, CancellationToken cancellationToken);

    Task<GameExecutable> AddExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken);

    Task SetPrimaryExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
