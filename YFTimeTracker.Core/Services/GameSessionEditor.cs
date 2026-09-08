using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Core.Services;

public sealed class GameSessionEditor(
    IGameRepository games,
    IGameSessionRepository sessions,
    IBootSessionProvider bootSessionProvider,
    IClock clock) : IGameSessionEditor
{
    public async Task<GameSession> AddManualSessionAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken)
    {
        await EnsureValidSessionAsync(gameId, startedAtUtc, endedAtUtc, null, cancellationToken);

        return await sessions.AddAsync(new GameSession
        {
            GameId = gameId,
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = endedAtUtc,
            EndedAtUtc = endedAtUtc,
            DurationSeconds = Convert.ToInt64(Math.Floor((endedAtUtc - startedAtUtc).TotalSeconds)),
            BootSessionId = bootSessionProvider.GetCurrentBootSessionId()
        }, cancellationToken);
    }

    public async Task UpdateManualSessionAsync(long sessionId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, CancellationToken cancellationToken)
    {
        var session = await sessions.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new YFTimeTrackerException("Die Session wurde nicht gefunden.");

        if (session.IsOpen)
        {
            throw new YFTimeTrackerException("Eine laufende Session kann nicht bearbeitet werden. Pausiere zuerst das Tracking oder beende das Spiel.");
        }

        await EnsureValidSessionAsync(session.GameId, startedAtUtc, endedAtUtc, sessionId, cancellationToken);

        session.StartedAtUtc = startedAtUtc;
        session.LastSeenAtUtc = endedAtUtc;
        session.EndedAtUtc = endedAtUtc;
        session.DurationSeconds = Convert.ToInt64(Math.Floor((endedAtUtc - startedAtUtc).TotalSeconds));

        await sessions.UpdateAsync(session, cancellationToken);
    }

    public async Task MoveSessionAsync(long sessionId, long targetGameId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new YFTimeTrackerException("Die Session wurde nicht gefunden.");

        if (session.IsOpen)
        {
            throw new YFTimeTrackerException("Eine laufende Session kann nicht verschoben werden. Pausiere zuerst das Tracking oder beende das Spiel.");
        }

        if (session.GameId == targetGameId)
        {
            throw new YFTimeTrackerException("Die Session liegt bereits bei diesem Spiel.");
        }

        if (await games.GetByIdAsync(targetGameId, cancellationToken) is null)
        {
            throw new YFTimeTrackerException("Das Spiel wurde nicht gefunden.");
        }

        if (await sessions.HasOverlapAsync(targetGameId, session.StartedAtUtc, session.EndedAtUtc!.Value, null, cancellationToken))
        {
            throw new YFTimeTrackerException("Diese Session überschneidet sich mit einer bestehenden Session des Zielspiels.");
        }

        session.GameId = targetGameId;
        session.Game = null;
        await sessions.UpdateAsync(session, cancellationToken);
    }

    public async Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetByIdAsync(sessionId, cancellationToken)
            ?? throw new YFTimeTrackerException("Die Session wurde nicht gefunden.");

        if (session.IsOpen)
        {
            throw new YFTimeTrackerException("Eine laufende Session kann nicht gelöscht werden. Pausiere zuerst das Tracking oder beende das Spiel.");
        }

        await sessions.DeleteAsync(sessionId, cancellationToken);
    }

    private async Task EnsureValidSessionAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, long? excludedSessionId, CancellationToken cancellationToken)
    {
        if (await games.GetByIdAsync(gameId, cancellationToken) is null)
        {
            throw new YFTimeTrackerException("Das Spiel wurde nicht gefunden.");
        }

        if (endedAtUtc <= startedAtUtc)
        {
            throw new YFTimeTrackerException("Das Session-Ende muss nach dem Start liegen.");
        }

        if (endedAtUtc > clock.UtcNow)
        {
            throw new YFTimeTrackerException("Das Session-Ende darf nicht in der Zukunft liegen.");
        }

        if (await sessions.HasOverlapAsync(gameId, startedAtUtc, endedAtUtc, excludedSessionId, cancellationToken))
        {
            throw new YFTimeTrackerException("Diese Session überschneidet sich mit einer bestehenden Session desselben Spiels.");
        }
    }
}
