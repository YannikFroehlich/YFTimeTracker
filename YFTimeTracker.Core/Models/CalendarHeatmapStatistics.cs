namespace YFTimeTracker.Core.Models;

public sealed record CalendarHeatmapStatistics(
    int Year,
    IReadOnlyList<int> AvailableYears,
    IReadOnlyList<DailyPlaytimeInfo> Days);
