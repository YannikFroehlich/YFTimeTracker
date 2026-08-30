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

    public async Task<PlaytimeStatistics> GetStatisticsAsync(
        StatisticsPeriodKind period,
        TimeZoneInfo localTimeZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localTimeZone);

        var allSessions = await sessions.GetSessionsAsync(null, null, cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, localTimeZone).Date);
        var range = CreateStatisticsRange(period, today, allSessions, localTimeZone);
        var rangeStartUtc = LocalDateStartToUtc(range.Start, localTimeZone);
        var rangeEndUtc = LocalDateStartToUtc(range.EndExclusive, localTimeZone);

        var contributions = allSessions
            .Select(session => new SessionContribution(
                session,
                GetOverlap(session, rangeStartUtc, rangeEndUtc)))
            .Where(contribution => contribution.Duration > TimeSpan.Zero)
            .ToArray();

        var totalDuration = TimeSpan.FromTicks(contributions.Sum(contribution => contribution.Duration.Ticks));
        var sessionCount = contributions.Length;
        var averageDuration = sessionCount == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(totalDuration.Ticks / sessionCount);
        var longest = contributions
            .OrderByDescending(contribution => contribution.Duration)
            .FirstOrDefault();

        var timeline = CreateTimeline(range, allSessions, localTimeZone);
        var games = contributions
            .GroupBy(contribution => contribution.Session.GameId)
            .Select(group =>
            {
                var game = group.Select(contribution => contribution.Session.Game).FirstOrDefault(candidate => candidate is not null);
                return new GamePlaytimeStatistics(
                    group.Key,
                    game?.Name ?? "Unbekanntes Spiel",
                    game?.Source ?? GameSource.Manual,
                    TimeSpan.FromTicks(group.Sum(contribution => contribution.Duration.Ticks)),
                    group.Count(),
                    group.Max(contribution => GetEffectiveSessionEnd(contribution.Session)));
            })
            .OrderByDescending(game => game.Duration)
            .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        TimeSpan? previousPeriodDuration = null;
        if (range.PreviousStart is { } previousStart && range.PreviousEndExclusive is { } previousEnd)
        {
            previousPeriodDuration = GetDurationForUtcRange(
                allSessions,
                LocalDateStartToUtc(previousStart, localTimeZone),
                LocalDateStartToUtc(previousEnd, localTimeZone));
        }

        return new PlaytimeStatistics(
            period,
            range.Start,
            range.EndExclusive,
            totalDuration,
            previousPeriodDuration,
            sessionCount,
            games.Length,
            averageDuration,
            longest?.Duration ?? TimeSpan.Zero,
            longest?.Session.Game?.Name,
            timeline,
            games,
            CreateWeekdayDistribution(contributions, rangeStartUtc, rangeEndUtc, localTimeZone));
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

    private StatisticsRange CreateStatisticsRange(
        StatisticsPeriodKind period,
        DateOnly today,
        IReadOnlyList<GameSession> allSessions,
        TimeZoneInfo localTimeZone)
    {
        return period switch
        {
            StatisticsPeriodKind.Last7Days => CreateDayRange(period, today, 7),
            StatisticsPeriodKind.Last30Days => CreateDayRange(period, today, 30),
            StatisticsPeriodKind.Last12Months => CreateMonthRange(today),
            StatisticsPeriodKind.AllTime => CreateAllTimeRange(today, allSessions, localTimeZone),
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, null)
        };
    }

    private static StatisticsRange CreateDayRange(StatisticsPeriodKind period, DateOnly today, int dayCount)
    {
        var start = today.AddDays(-(dayCount - 1));
        var endExclusive = today.AddDays(1);
        return new StatisticsRange(
            period,
            start,
            endExclusive,
            StatisticsBucketKind.Day,
            start.AddDays(-dayCount),
            start);
    }

    private static StatisticsRange CreateMonthRange(DateOnly today)
    {
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var start = currentMonth.AddMonths(-11);
        return new StatisticsRange(
            StatisticsPeriodKind.Last12Months,
            start,
            currentMonth.AddMonths(1),
            StatisticsBucketKind.Month,
            start.AddMonths(-12),
            start);
    }

    private StatisticsRange CreateAllTimeRange(
        DateOnly today,
        IReadOnlyList<GameSession> allSessions,
        TimeZoneInfo localTimeZone)
    {
        var earliestDate = allSessions.Count == 0
            ? today
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                allSessions.Min(session => session.StartedAtUtc),
                localTimeZone).Date);
        var dayCount = today.DayNumber - earliestDate.DayNumber + 1;
        var bucketKind = dayCount <= 31 ? StatisticsBucketKind.Day : StatisticsBucketKind.Month;
        var start = bucketKind == StatisticsBucketKind.Day
            ? earliestDate
            : new DateOnly(earliestDate.Year, earliestDate.Month, 1);
        var endExclusive = bucketKind == StatisticsBucketKind.Day
            ? today.AddDays(1)
            : new DateOnly(today.Year, today.Month, 1).AddMonths(1);

        return new StatisticsRange(
            StatisticsPeriodKind.AllTime,
            start,
            endExclusive,
            bucketKind,
            null,
            null);
    }

    private IReadOnlyList<StatisticsTimelinePoint> CreateTimeline(
        StatisticsRange range,
        IReadOnlyList<GameSession> allSessions,
        TimeZoneInfo localTimeZone)
    {
        var points = new List<StatisticsTimelinePoint>();
        for (var bucketStart = range.Start; bucketStart < range.EndExclusive;)
        {
            var bucketEnd = range.BucketKind == StatisticsBucketKind.Day
                ? bucketStart.AddDays(1)
                : bucketStart.AddMonths(1);
            if (bucketEnd > range.EndExclusive)
            {
                bucketEnd = range.EndExclusive;
            }

            points.Add(new StatisticsTimelinePoint(
                bucketStart,
                bucketEnd,
                range.BucketKind,
                GetDurationForUtcRange(
                    allSessions,
                    LocalDateStartToUtc(bucketStart, localTimeZone),
                    LocalDateStartToUtc(bucketEnd, localTimeZone))));
            bucketStart = bucketEnd;
        }

        return points;
    }

    private IReadOnlyList<WeekdayPlaytimeStatistics> CreateWeekdayDistribution(
        IReadOnlyList<SessionContribution> contributions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        TimeZoneInfo localTimeZone)
    {
        var totals = Enum.GetValues<DayOfWeek>().ToDictionary(day => day, _ => TimeSpan.Zero);

        foreach (var contribution in contributions)
        {
            var effectiveEnd = GetEffectiveSessionEnd(contribution.Session);
            var overlapStart = contribution.Session.StartedAtUtc > rangeStartUtc
                ? contribution.Session.StartedAtUtc
                : rangeStartUtc;
            var overlapEnd = effectiveEnd < rangeEndUtc ? effectiveEnd : rangeEndUtc;
            var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(overlapStart, localTimeZone).Date);
            var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(overlapEnd.AddTicks(-1), localTimeZone).Date);

            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                var duration = GetOverlap(
                    contribution.Session,
                    LocalDateStartToUtc(date, localTimeZone),
                    LocalDateStartToUtc(date.AddDays(1), localTimeZone));
                totals[date.DayOfWeek] += duration;
            }
        }

        return Enumerable.Range(0, 7)
            .Select(offset => (DayOfWeek)(((int)DayOfWeek.Monday + offset) % 7))
            .Select(day => new WeekdayPlaytimeStatistics(day, totals[day]))
            .ToArray();
    }

    private TimeSpan GetOverlap(
        GameSession session,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var sessionEnd = GetEffectiveSessionEnd(session);
        var overlapStart = session.StartedAtUtc > rangeStartUtc ? session.StartedAtUtc : rangeStartUtc;
        var overlapEnd = sessionEnd < rangeEndUtc ? sessionEnd : rangeEndUtc;
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
    }

    private DateTimeOffset GetEffectiveSessionEnd(GameSession session) => session.EndedAtUtc ?? clock.UtcNow;

    private TimeSpan GetDurationForUtcRange(
        IEnumerable<GameSession> relevantSessions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var total = TimeSpan.Zero;
        foreach (var session in relevantSessions)
        {
            total += GetOverlap(session, rangeStartUtc, rangeEndUtc);
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

    private sealed record StatisticsRange(
        StatisticsPeriodKind Period,
        DateOnly Start,
        DateOnly EndExclusive,
        StatisticsBucketKind BucketKind,
        DateOnly? PreviousStart,
        DateOnly? PreviousEndExclusive);

    private sealed record SessionContribution(GameSession Session, TimeSpan Duration);
}
