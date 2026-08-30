namespace YFTimeTracker.Core.Models;

public enum StatisticsPeriodKind
{
    Last7Days,
    Last30Days,
    Last12Months,
    AllTime
}

public enum StatisticsBucketKind
{
    Day,
    Month
}

public sealed record PlaytimeStatistics(
    StatisticsPeriodKind Period,
    DateOnly PeriodStart,
    DateOnly PeriodEndExclusive,
    TimeSpan TotalDuration,
    TimeSpan? PreviousPeriodDuration,
    int SessionCount,
    int GamesPlayedCount,
    TimeSpan AverageSessionDuration,
    TimeSpan LongestSessionDuration,
    string? LongestSessionGameName,
    IReadOnlyList<StatisticsTimelinePoint> Timeline,
    IReadOnlyList<GamePlaytimeStatistics> Games,
    IReadOnlyList<WeekdayPlaytimeStatistics> Weekdays);

public sealed record StatisticsTimelinePoint(
    DateOnly StartDate,
    DateOnly EndDateExclusive,
    StatisticsBucketKind BucketKind,
    TimeSpan Duration);

public sealed record GamePlaytimeStatistics(
    long GameId,
    string Name,
    GameSource Source,
    TimeSpan Duration,
    int SessionCount,
    DateTimeOffset LastPlayedAtUtc);

public sealed record WeekdayPlaytimeStatistics(DayOfWeek DayOfWeek, TimeSpan Duration);
