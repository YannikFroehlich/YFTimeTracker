using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGlobalSearchRepository
{
    Task<GlobalSearchResults> SearchAsync(
        GlobalSearchQuery query,
        int gameCount,
        int sessionCount,
        CancellationToken cancellationToken);
}
