using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Repositories;

public sealed class PlaytimeReadRepository(IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : IPlaytimeReadRepository
{
    public async Task<PlaytimeOverview> GetOverviewAsync(
        DateTimeOffset nowUtc,
        int recentGameCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recentGameCount, 1);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var totalDurationSeconds = await GetTotalDurationSecondsAsync(context, nowUtc, cancellationToken);
        var gamesPlayedCount = await context.GameSessions
            .Select(session => session.GameId)
            .Distinct()
            .CountAsync(cancellationToken);

        var recentRows = await context.GameSessions
            .Where(session => session.Game != null)
            .GroupBy(session => new { session.GameId, session.Game!.Name })
            .Select(group => new
            {
                group.Key.GameId,
                group.Key.Name,
                LastPlayedAtUtc = group.Max(session => session.EndedAtUtc ?? nowUtc),
                StoredDurationSeconds = group.Sum(session => session.DurationSeconds ?? 0L),
                IsRunning = group.Any(session => session.EndedAtUtc == null)
            })
            .OrderByDescending(row => row.LastPlayedAtUtc)
            .ThenBy(row => row.Name)
            .Take(recentGameCount)
            .ToListAsync(cancellationToken);

        var recentGameIds = recentRows.Select(row => row.GameId).ToArray();
        var unresolvedDurations = recentGameIds.Length == 0
            ? []
            : await context.GameSessions
                .Where(session => recentGameIds.Contains(session.GameId) && session.DurationSeconds == null)
                .Select(session => new SessionTiming(
                    session.GameId,
                    session.StartedAtUtc,
                    session.EndedAtUtc))
                .ToListAsync(cancellationToken);

        var unresolvedByGame = unresolvedDurations
            .GroupBy(session => session.GameId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(session => GetEffectiveSeconds(session.StartedAtUtc, session.EndedAtUtc, nowUtc)));

        var recentGames = recentRows
            .Select(row => new RecentGameInfo(
                row.GameId,
                row.Name,
                row.LastPlayedAtUtc,
                TimeSpan.FromSeconds(row.StoredDurationSeconds + unresolvedByGame.GetValueOrDefault(row.GameId)),
                row.IsRunning))
            .ToArray();

        return new PlaytimeOverview(totalDurationSeconds, gamesPlayedCount, recentGames);
    }

    public async Task<long> GetTotalDurationSecondsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await GetTotalDurationSecondsAsync(context, nowUtc, cancellationToken);
    }

    public async Task<DateTimeOffset?> GetEarliestSessionStartAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameSessions
            .Select(session => (DateTimeOffset?)session.StartedAtUtc)
            .MinAsync(cancellationToken);
    }

    private static async Task<long> GetTotalDurationSecondsAsync(
        YFTimeTrackerDbContext context,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var storedDurationSeconds = await context.GameSessions
            .Where(session => session.DurationSeconds != null)
            .SumAsync(session => session.DurationSeconds ?? 0L, cancellationToken);
        var unresolvedDurations = await context.GameSessions
            .Where(session => session.DurationSeconds == null)
            .Select(session => new SessionTiming(
                session.GameId,
                session.StartedAtUtc,
                session.EndedAtUtc))
            .ToListAsync(cancellationToken);

        return storedDurationSeconds + unresolvedDurations.Sum(session =>
            GetEffectiveSeconds(session.StartedAtUtc, session.EndedAtUtc, nowUtc));
    }

    private static long GetEffectiveSeconds(
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        DateTimeOffset nowUtc)
    {
        var effectiveEnd = endedAtUtc ?? nowUtc;
        return effectiveEnd <= startedAtUtc
            ? 0
            : Convert.ToInt64(Math.Floor((effectiveEnd - startedAtUtc).TotalSeconds));
    }

    private sealed record SessionTiming(
        long GameId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc);
}
