namespace YFTimeTracker.Core.Models;

public sealed record YearReview(
    int Year,
    TimeSpan TotalDuration,
    TimeSpan PreviousYearDuration,
    int SessionCount,
    int GamesPlayedCount,
    int ActiveDayCount,
    TimeSpan LongestSessionDuration,
    string? LongestSessionGameName,
    DateTimeOffset? LongestSessionStartedAtUtc,
    YearReviewMonth? MostActiveMonth,
    IReadOnlyList<YearReviewMonth> Months,
    IReadOnlyList<YearReviewGame> Games);

public sealed record YearReviewMonth(int Month, TimeSpan Duration);

public sealed record YearReviewGame(
    long GameId,
    string Name,
    GameSource Source,
    TimeSpan Duration,
    int SessionCount,
    string? ExecutablePath);
