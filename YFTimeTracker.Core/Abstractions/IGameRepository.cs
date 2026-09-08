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

    /// <summary>
    /// Hängt EXE-Zuordnungen und Sessions des Quellspiels auf das Zielspiel um und löscht das
    /// Quellspiel - in einer Transaktion. Sessions gehören normalerweise ins Session-Repository,
    /// lassen sich hier aber nicht abtrennen: der Cascade auf GameSessions würde beim Löschen des
    /// Quellspiels genau die Spielzeit mitnehmen, die erhalten bleiben soll.
    /// </summary>
    Task MergeIntoAsync(long sourceGameId, long targetGameId, SessionMergePlan plan, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
