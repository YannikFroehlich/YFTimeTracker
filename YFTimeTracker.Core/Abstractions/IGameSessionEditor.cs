using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameSessionEditor
{
    Task<GameSession> AddManualSessionAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken);

    Task UpdateManualSessionAsync(long sessionId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken);

    Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken);
}
