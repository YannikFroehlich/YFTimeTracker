using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameCatalogService
{
    Task<IReadOnlyList<Game>> GetGamesAsync(CancellationToken cancellationToken);

    Task<Game> AddGameAsync(string executablePath, string? displayName, CancellationToken cancellationToken);

    Task UpdateGameAsync(
        long gameId,
        string displayName,
        string executablePath,
        int? dailyPlaytimeLimitMinutes,
        int? weeklyPlaytimeLimitMinutes,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken);

    Task<GameMergeResult> MergeGamesAsync(long sourceGameId, long targetGameId, CancellationToken cancellationToken);

    Task DeleteGameAsync(long gameId, CancellationToken cancellationToken);

    Task SetPinnedAsync(long gameId, bool isPinned, CancellationToken cancellationToken);
}
