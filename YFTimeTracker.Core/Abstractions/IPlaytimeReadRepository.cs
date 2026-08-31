using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IPlaytimeReadRepository
{
    Task<PlaytimeOverview> GetOverviewAsync(DateTimeOffset nowUtc, int recentGameCount, CancellationToken cancellationToken);

    Task<long> GetTotalDurationSecondsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetEarliestSessionStartAsync(CancellationToken cancellationToken);
}
