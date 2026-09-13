using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface ITrackingExclusionService
{
    Task<IReadOnlyList<TrackingExclusionRule>> GetRulesAsync(CancellationToken cancellationToken);

    Task<TrackingExclusionRule> AddAsync(
        TrackingExclusionKind kind,
        string path,
        CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    TrackingExclusionRule? FindMatch(
        RunningProcessInfo process,
        IReadOnlyList<TrackingExclusionRule> rules);
}
