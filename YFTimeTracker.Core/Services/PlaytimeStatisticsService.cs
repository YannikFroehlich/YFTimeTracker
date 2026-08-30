using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

public sealed class PlaytimeStatisticsService(
    IGameSessionRepository sessions,
    IClock clock) : IPlaytimeStatisticsService
{
    public async Task<DashboardStats> GetDashboardStatsAsync(TimeZoneInfo localTimeZone, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, localTimeZone).Date);
        var weekStart = GetIsoWeekStart(today);
        var allSessions = await sessions.GetSessionsAsync(null, null, cancellationToken);

        var todayStartUtc = LocalDateStartToUtc(today, localTimeZone);
        var tomorrowStartUtc = LocalDateStartToUtc(today.AddDays(1), localTimeZone);
        var previousDayStartUtc = LocalDateStartToUtc(today.AddDays(-1), localTimeZone);
        var weekStartUtc = LocalDateStartToUtc(weekStart, localTimeZone);
        var weekEndUtc = LocalDateStartToUtc(weekStart.AddDays(7), localTimeZone);
        var previousWeekStartUtc = LocalDateStartToUtc(weekStart.AddDays(-7), localTimeZone);

        var todayDuration = GetDurationForUtcRange(allSessions, todayStartUtc, tomorrowStartUtc);
        var previousDayDuration = GetDurationForUtcRange(allSessions, previousDayStartUtc, todayStartUtc);
        var weekDuration = GetDurationForUtcRange(allSessions, weekStartUtc, weekEndUtc);
        var previousWeekDuration = GetDurationForUtcRange(allSessions, previousWeekStartUtc, weekStartUtc);
        var total = TimeSpan.FromSeconds(allSessions.Sum(GetStoredOrEffectiveSeconds));

        var running = allSessions
            .Where(session => session.IsOpen)
            .Where(session => session.Game is not null)
            .Select(session => new RunningGameInfo(
                session.GameId,
                session.Game!.Name,
                session.StartedAtUtc,
                session.GetEffectiveDuration(clock.UtcNow)))
            .OrderBy(info => info.Name)
            .ToArray();

        var recent = allSessions
            .Where(session => session.Game is not null)
            .GroupBy(session => session.GameId)
            .Select(group => new RecentGameInfo(
                group.Key,
                group.Select(session => session.Game!.Name).First(),
                group.Max(session => session.EndedAtUtc ?? (session.IsOpen ? clock.UtcNow : session.LastSeenAtUtc)),
                TimeSpan.FromSeconds(group.Sum(GetStoredOrEffectiveSeconds)),
                group.Any(session => session.IsOpen)))
            .OrderByDescending(info => info.LastPlayedAtUtc)
            .Take(8)
            .ToArray();

        var currentWeekDays = Enumerable.Range(0, 7)
            .Select(offset => weekStart.AddDays(offset))
            .Select(date => new DailyPlaytimeInfo(
                date,
                GetDurationForUtcRange(
                    allSessions,
                    LocalDateStartToUtc(date, localTimeZone),
                    LocalDateStartToUtc(date.AddDays(1), localTimeZone))))
            .ToArray();

        return new DashboardStats(
            todayDuration,
            previousDayDuration,
            weekDuration,
            previousWeekDuration,
            total,
            CountSessionsInUtcRange(allSessions, todayStartUtc, tomorrowStartUtc),
            CountSessionsInUtcRange(allSessions, weekStartUtc, weekEndUtc),
            allSessions.Select(session => session.GameId).Distinct().Count(),
            currentWeekDays,
            running,
            recent);
    }

    public async Task<TimeSpan> GetTotalDurationAsync(CancellationToken cancellationToken)
    {
        var allSessions = await sessions.GetSessionsAsync(null, null, cancellationToken);
        var totalSeconds = allSessions.Sum(GetStoredOrEffectiveSeconds);
        return TimeSpan.FromSeconds(totalSeconds);
    }

    public async Task<TimeSpan> GetDurationForLocalRangeAsync(DateOnly localStart, DateOnly localEndExclusive, TimeZoneInfo localTimeZone, CancellationToken cancellationToken)
    {
        if (localEndExclusive <= localStart)
        {
            return TimeSpan.Zero;
        }

        var rangeStartUtc = LocalDateStartToUtc(localStart, localTimeZone);
        var rangeEndUtc = LocalDateStartToUtc(localEndExclusive, localTimeZone);
        var relevantSessions = await sessions.GetSessionsAsync(rangeStartUtc, rangeEndUtc, cancellationToken);

        return GetDurationForUtcRange(relevantSessions, rangeStartUtc, rangeEndUtc);
    }

    public static DateOnly GetIsoWeekStart(DateOnly date)
    {
        var dayOfWeek = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        return date.AddDays(1 - dayOfWeek);
    }

    private static DateTimeOffset LocalDateStartToUtc(DateOnly localDate, TimeZoneInfo localTimeZone)
    {
        var localDateTime = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = localTimeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }

    private TimeSpan GetDurationForUtcRange(
        IEnumerable<GameSession> relevantSessions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var total = TimeSpan.Zero;
        foreach (var session in relevantSessions)
        {
            var sessionEnd = session.EndedAtUtc ?? clock.UtcNow;
            var overlapStart = session.StartedAtUtc > rangeStartUtc ? session.StartedAtUtc : rangeStartUtc;
            var overlapEnd = sessionEnd < rangeEndUtc ? sessionEnd : rangeEndUtc;

            if (overlapEnd > overlapStart)
            {
                total += overlapEnd - overlapStart;
            }
        }

        return total;
    }

    private int CountSessionsInUtcRange(
        IEnumerable<GameSession> relevantSessions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        return relevantSessions.Count(session =>
        {
            var sessionEnd = session.EndedAtUtc ?? clock.UtcNow;
            return session.StartedAtUtc < rangeEndUtc && sessionEnd > rangeStartUtc;
        });
    }

    private long GetStoredOrEffectiveSeconds(GameSession session)
    {
        return session.DurationSeconds
            ?? Math.Max(0, Convert.ToInt64(Math.Floor(session.GetEffectiveDuration(clock.UtcNow).TotalSeconds)));
    }
}
