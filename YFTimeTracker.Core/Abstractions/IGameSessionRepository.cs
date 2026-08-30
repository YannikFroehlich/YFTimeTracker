using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameSessionRepository
{
    Task<GameSession?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameSession>> GetOpenSessionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GameSession>> GetSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GameSession>> GetRecentCompletedSessionsAsync(int count, CancellationToken cancellationToken);

    Task<GameSession> AddAsync(GameSession session, CancellationToken cancellationToken);

    Task UpdateAsync(GameSession session, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    Task<bool> HasOverlapAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, long? excludedSessionId, CancellationToken cancellationToken);
}
