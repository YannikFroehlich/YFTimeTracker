using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Repositories;

public sealed class GameRepository(IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : IGameRepository
{
    public async Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
            .AsNoTracking()
            .OrderBy(game => game.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Game?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
            .AsNoTracking()
            .FirstOrDefaultAsync(game => game.Id == id, cancellationToken);
    }

    public async Task<Game?> GetByExecutablePathKeyAsync(string executablePathKey, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
            .AsNoTracking()
            .FirstOrDefaultAsync(game => game.Executables.Any(executable => executable.ExecutablePathKey == executablePathKey), cancellationToken);
    }

    public async Task<Game?> GetByExternalIdAsync(GameSource source, string externalGameId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Games
            .Include(game => game.Executables)
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
