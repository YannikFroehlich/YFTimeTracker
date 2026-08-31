using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Repositories;

public sealed class GlobalSearchRepository(IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : IGlobalSearchRepository
{
    public async Task<GlobalSearchResults> SearchAsync(
        string searchText,
        int gameCount,
        int sessionCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gameCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionCount, 1);

        var trimmedSearch = searchText.Trim();
        if (trimmedSearch.Length < 2)
        {
            return GlobalSearchResults.Empty;
        }

        var pattern = CreateContainsPattern(trimmedSearch);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var games = await context.Games
            .Include(game => game.Executables)
            .AsNoTracking()
            .Where(game =>
                EF.Functions.Like(game.Name, pattern, "\\")
                || (game.InstallDirectory != null && EF.Functions.Like(game.InstallDirectory, pattern, "\\"))
                || game.Executables.Any(executable =>
                    EF.Functions.Like(executable.ExecutableName, pattern, "\\")
                    || EF.Functions.Like(executable.ExecutablePath, pattern, "\\")))
            .OrderBy(game => game.Name.StartsWith(trimmedSearch) ? 0 : 1)
            .ThenBy(game => game.Name)
            .Take(gameCount)
            .ToListAsync(cancellationToken);

        var sessions = await context.GameSessions
            .Include(session => session.Game)
            .ThenInclude(game => game!.Executables)
            .AsNoTracking()
            .Where(session => session.Game != null && (
                EF.Functions.Like(session.Game.Name, pattern, "\\")
                || session.Game.Executables.Any(executable =>
                    EF.Functions.Like(executable.ExecutableName, pattern, "\\"))))
            .OrderByDescending(session => session.StartedAtUtc)
            .Take(sessionCount)
            .ToListAsync(cancellationToken);

        return new GlobalSearchResults(games, sessions);
    }

    private static string CreateContainsPattern(string searchText)
    {
        var escaped = searchText
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
