using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Data.Repositories;

public sealed class GlobalSearchRepository(IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : IGlobalSearchRepository
{
    public async Task<GlobalSearchResults> SearchAsync(
        GlobalSearchQuery query,
        int gameCount,
        int sessionCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gameCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sessionCount, 1);

        ArgumentNullException.ThrowIfNull(query);

        var trimmedSearch = query.SearchText.Trim();
        if (trimmedSearch.Length < 2)
        {
            return GlobalSearchResults.Empty;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var gameQuery = context.Games
            .Include(game => game.Executables)
            .AsNoTracking();
        if (query.Source is { } source)
        {
            gameQuery = gameQuery.Where(game => game.Source == source);
        }

        var candidates = await gameQuery.ToListAsync(cancellationToken);
        var rankedGames = candidates
            .Select(game => new
            {
                Game = game,
                Score = GetGameScore(trimmedSearch, game)
            })
            .Where(result => result.Score is not null)
            .OrderBy(result => result.Score)
            .ThenBy(result => result.Game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var games = rankedGames
            .Take(gameCount)
            .Select(result => result.Game)
            .ToList();

        var matchingGameIds = rankedGames.Select(result => result.Game.Id).ToArray();
        if (matchingGameIds.Length == 0)
        {
            return new GlobalSearchResults(games, []);
        }

        var sessionQuery = context.GameSessions
            .Include(session => session.Game)
            .ThenInclude(game => game!.Executables)
            .AsNoTracking()
            .Where(session => matchingGameIds.Contains(session.GameId));
        if (query.SessionsSinceUtc is { } sessionsSinceUtc)
        {
            sessionQuery = sessionQuery.Where(session => session.StartedAtUtc >= sessionsSinceUtc);
        }

        var sessions = await sessionQuery
            .OrderByDescending(session => session.StartedAtUtc)
            .Take(sessionCount)
            .ToListAsync(cancellationToken);

        return new GlobalSearchResults(games, sessions);
    }

    private static int? GetGameScore(string searchText, Game game)
    {
        var scores = new List<int?>
        {
            FuzzySearchMatcher.GetScore(searchText, game.Name),
            AddPenalty(FuzzySearchMatcher.GetScore(searchText, game.InstallDirectory), 8)
        };
        scores.AddRange(game.Executables.SelectMany(executable => new[]
        {
            AddPenalty(FuzzySearchMatcher.GetScore(searchText, executable.ExecutableName), 4),
            AddPenalty(FuzzySearchMatcher.GetScore(searchText, executable.ExecutablePath), 6)
        }));

        return scores.Where(score => score is not null).Min();
    }

    private static int? AddPenalty(int? score, int penalty)
    {
        return score is { } value ? value + penalty : null;
    }
}
