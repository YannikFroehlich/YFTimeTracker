using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGlobalSearchRepository
{
    Task<GlobalSearchResults> SearchAsync(
        string searchText,
        int gameCount,
        int sessionCount,
        CancellationToken cancellationToken);
}
