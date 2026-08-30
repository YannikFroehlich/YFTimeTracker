using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IPlaytimeStatisticsService
{
    Task<DashboardStats> GetDashboardStatsAsync(TimeZoneInfo localTimeZone, CancellationToken cancellationToken);

    Task<TimeSpan> GetTotalDurationAsync(CancellationToken cancellationToken);

    Task<TimeSpan> GetDurationForLocalRangeAsync(DateOnly localStart, DateOnly localEndExclusive, TimeZoneInfo localTimeZone, CancellationToken cancellationToken);
}
