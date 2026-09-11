using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Data.Repositories;

public sealed class GameRepository(IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : IGameRepository
{
    public async Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
            .Include(game => game.Tags)
            .AsNoTracking()
            .OrderBy(game => game.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Game?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
            .Include(game => game.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(game => game.Id == id, cancellationToken);
    }

    public async Task<Game?> GetByExecutablePathKeyAsync(string executablePathKey, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
            .Include(game => game.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(game => game.Executables.Any(executable => executable.ExecutablePathKey == executablePathKey), cancellationToken);
    }

    public async Task<Game?> GetByExternalIdAsync(GameSource source, string externalGameId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
            .Include(game => game.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(game => game.Source == source && game.ExternalGameId == externalGameId, cancellationToken);
    }

    public async Task<Game> AddAsync(Game game, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        SynchronizeLegacyExecutable(game);
        context.Games.Add(game);
        await context.SaveChangesAsync(cancellationToken);
        return game;
    }

    public async Task UpdateAsync(Game game, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        SynchronizeLegacyExecutable(game);
        context.Games.Update(game);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<GameExecutable> AddExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        executable.GameId = gameId;
        executable.Game = null;
        context.GameExecutables.Add(executable);
        await context.SaveChangesAsync(cancellationToken);
        return executable;
    }

    public async Task SetPrimaryExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existingPrimary = await context.GameExecutables
            .FirstOrDefaultAsync(candidate => candidate.GameId == gameId && candidate.IsPrimary, cancellationToken);
        var target = await context.GameExecutables
            .FirstOrDefaultAsync(candidate => candidate.ExecutablePathKey == executable.ExecutablePathKey, cancellationToken);

        if (target is not null && target.GameId != gameId)
        {
            throw new InvalidOperationException("Executable path is already assigned to another game.");
        }

        if (existingPrimary is not null && (target is null || existingPrimary.Id != target.Id))
        {
            existingPrimary.IsPrimary = false;
        }

        if (target is null)
        {
            executable.GameId = gameId;
            executable.Game = null;
            executable.IsPrimary = true;
            context.GameExecutables.Add(executable);
        }
        else
        {
            target.ExecutablePath = executable.ExecutablePath;
            target.ExecutableName = executable.ExecutableName;
            target.IsPrimary = true;
        }

        var game = await context.Games.FirstAsync(candidate => candidate.Id == gameId, cancellationToken);
        game.LegacyExecutablePath = executable.ExecutablePath;
        game.LegacyExecutablePathKey = executable.ExecutablePathKey;
        game.LegacyExecutableName = executable.ExecutableName;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetPinnedAsync(long gameId, bool isPinned, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var game = await context.Games.FirstOrDefaultAsync(candidate => candidate.Id == gameId, cancellationToken)
            ?? throw new YFTimeTrackerException("Das Spiel wurde nicht gefunden.");
        game.IsPinned = isPinned;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTagsAsync(long gameId, IReadOnlyList<string> tags, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.GameTags
            .Where(tag => tag.GameId == gameId)
            .ToListAsync(cancellationToken);
        context.GameTags.RemoveRange(existing);
        context.GameTags.AddRange(tags.Select(tag => new GameTag { GameId = gameId, Tag = tag }));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MergeIntoAsync(long sourceGameId, long targetGameId, SessionMergePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var removedIds = plan.RemovedSessionIds.ToHashSet();
        if (removedIds.Count > 0)
        {
            var absorbed = await context.GameSessions
                .Where(session => removedIds.Contains(session.Id))
                .ToListAsync(cancellationToken);
            context.GameSessions.RemoveRange(absorbed);
        }

        foreach (var update in plan.Updates)
        {
            var session = await context.GameSessions
                .FirstOrDefaultAsync(candidate => candidate.Id == update.SessionId, cancellationToken);
            if (session is null)
            {
                continue;
            }

            session.GameId = targetGameId;
            session.StartedAtUtc = update.StartedAtUtc;
            session.Close(update.EndedAtUtc);
        }

        // Sessions, die der Plan nicht anfasst, dürfen nicht am Quellspiel hängen bleiben - sonst
        // nimmt der Cascade sie beim Löschen mit.
        var remaining = await context.GameSessions
            .Where(session => session.GameId == sourceGameId && !removedIds.Contains(session.Id))
            .ToListAsync(cancellationToken);
        foreach (var session in remaining)
        {
            session.GameId = targetGameId;
        }

        // Das Zielspiel behält seine primäre EXE, deshalb kommen die übernommenen als weitere dazu.
        var executables = await context.GameExecutables
            .Where(executable => executable.GameId == sourceGameId)
            .ToListAsync(cancellationToken);
        foreach (var executable in executables)
        {
            executable.GameId = targetGameId;
            executable.IsPrimary = false;
        }

        // Tags des Quellspiels wandern mit, Duplikate (bereits am Ziel vorhanden) werden verworfen statt
        // gegen den Unique-Index (GameId, Tag) zu laufen.
        var targetTagNames = await context.GameTags
            .Where(tag => tag.GameId == targetGameId)
            .Select(tag => tag.Tag)
            .ToListAsync(cancellationToken);
        var sourceTags = await context.GameTags
            .Where(tag => tag.GameId == sourceGameId)
            .ToListAsync(cancellationToken);
        foreach (var tag in sourceTags)
        {
            if (targetTagNames.Contains(tag.Tag, StringComparer.OrdinalIgnoreCase))
            {
                context.GameTags.Remove(tag);
            }
            else
            {
                tag.GameId = targetGameId;
            }
        }

        if (await context.Games.FirstOrDefaultAsync(game => game.Id == sourceGameId, cancellationToken) is { } source)
        {
            if (source.IsPinned)
            {
                var target = await context.Games.FirstAsync(game => game.Id == targetGameId, cancellationToken);
                target.IsPinned = true;
            }

            context.Games.Remove(source);
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var game = await context.Games.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (game is null)
        {
            return;
        }

        context.Games.Remove(game);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static void SynchronizeLegacyExecutable(Game game)
    {
        var primary = game.PrimaryExecutable
            ?? throw new InvalidOperationException("Every game requires a primary executable.");
        game.LegacyExecutablePath = primary.ExecutablePath;
        game.LegacyExecutablePathKey = primary.ExecutablePathKey;
        game.LegacyExecutableName = primary.ExecutableName;
    }
}
