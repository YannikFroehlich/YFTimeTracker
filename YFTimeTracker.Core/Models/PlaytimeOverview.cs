namespace YFTimeTracker.Core.Models;

public sealed record PlaytimeOverview(
    long TotalDurationSeconds,
    int GamesPlayedCount,
    IReadOnlyList<RecentGameInfo> RecentGames);
