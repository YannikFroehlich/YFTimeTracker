using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Repositories;

public sealed class GameSessionRepository(IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : IGameSessionRepository
{
    public async Task<GameSession?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameSessions
            .Include(session => session.Game)
            .ThenInclude(game => game!.Executables)
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<GameSession>> GetOpenSessionsAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameSessions
            .Include(session => session.Game)
            .ThenInclude(game => game!.Executables)
            .AsNoTracking()
            .Where(session => session.EndedAtUtc == null)
            .OrderBy(session => session.StartedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GameSession>> GetSessionsAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.GameSessions
            .Include(session => session.Game)
            .ThenInclude(game => game!.Executables)
            .AsNoTracking()
            .AsQueryable();

        if (fromUtc.HasValue)
        {
            query = query.Where(session => (session.EndedAtUtc ?? session.LastSeenAtUtc) > fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(session => session.StartedAtUtc < toUtc.Value);
        }

        return await query
            .OrderByDescending(session => session.StartedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GameSession>> GetSessionsForGameAsync(long gameId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameSessions
            .Include(session => session.Game)
            .ThenInclude(game => game!.Executables)
            .AsNoTracking()
            .Where(session => session.GameId == gameId)
            .OrderByDescending(session => session.StartedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GameSession>> GetRecentCompletedSessionsAsync(int count, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameSessions
            .Include(session => session.Game)
            .ThenInclude(game => game!.Executables)
            .AsNoTracking()
            .Where(session => session.EndedAtUtc != null)
            .OrderByDescending(session => session.EndedAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<GameSession> AddAsync(GameSession session, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.GameSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task UpdateAsync(GameSession session, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.GameSessions.Update(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var session = await context.GameSessions.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (session is null)
        {
            return;
        }

        context.GameSessions.Remove(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasOverlapAsync(long gameId, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, long? excludedSessionId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.GameSessions
            .AsNoTracking()
            .Where(session => session.GameId == gameId && session.EndedAtUtc != null);

        if (excludedSessionId.HasValue)
        {
            query = query.Where(session => session.Id != excludedSessionId.Value);
        }

        return await query.AnyAsync(session =>
            session.StartedAtUtc < endedAtUtc &&
            session.EndedAtUtc!.Value > startedAtUtc,
            cancellationToken);
    }
}
