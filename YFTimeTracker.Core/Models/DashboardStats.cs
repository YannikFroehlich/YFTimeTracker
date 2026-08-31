namespace YFTimeTracker.Core.Models;

public sealed record DashboardStats(
    TimeSpan Today,
    TimeSpan PreviousDay,
    TimeSpan CurrentWeek,
    TimeSpan PreviousWeek,
    TimeSpan Total,
    int TodaySessionCount,
    int CurrentWeekSessionCount,
    int GamesPlayedCount,
    IReadOnlyList<DailyPlaytimeInfo> CurrentWeekDays,
    IReadOnlyList<RunningGameInfo> RunningGames,
    IReadOnlyList<RecentGameInfo> RecentGames);

public sealed record DailyPlaytimeInfo(DateOnly Date, TimeSpan Duration);

public sealed record RunningGameInfo(
    long GameId,
    string Name,
    DateTimeOffset StartedAtUtc,
    TimeSpan Duration,
    string? ExecutablePath = null);

public sealed record RecentGameInfo(
    long GameId,
    string Name,
    DateTimeOffset LastPlayedAtUtc,
    TimeSpan TotalDuration,
    bool IsRunning,
    string? ExecutablePath = null);
