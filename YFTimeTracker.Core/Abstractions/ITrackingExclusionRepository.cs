using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface ITrackingExclusionRepository
{
    Task<IReadOnlyList<TrackingExclusionRule>> GetAllAsync(CancellationToken cancellationToken);

    Task<TrackingExclusionRule> AddAsync(TrackingExclusionRule rule, CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
