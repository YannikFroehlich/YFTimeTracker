using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IYearReviewService
{
    Task<IReadOnlyList<int>> GetAvailableYearsAsync(
        TimeZoneInfo localTimeZone,
        CancellationToken cancellationToken);

    Task<YearReview> GetYearReviewAsync(
        int year,
        TimeZoneInfo localTimeZone,
        CancellationToken cancellationToken);
}
