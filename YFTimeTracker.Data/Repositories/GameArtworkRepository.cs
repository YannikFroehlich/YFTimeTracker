using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Repositories;

public sealed class GameArtworkRepository(
    IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : IGameArtworkRepository
{
    public async Task<GameArtwork?> GetByGameIdAsync(long gameId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.GameArtworks
            .AsNoTracking()
            .FirstOrDefaultAsync(artwork => artwork.GameId == gameId, cancellationToken);
    }

    public async Task UpsertAsync(GameArtwork artwork, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.GameArtworks
            .FirstOrDefaultAsync(candidate => candidate.GameId == artwork.GameId, cancellationToken);
        if (existing is null)
        {
            artwork.Game = null;
            context.GameArtworks.Add(artwork);
        }
        else
        {
            existing.ContentType = artwork.ContentType;
            existing.FileExtension = artwork.FileExtension;
            existing.Sha256 = artwork.Sha256;
            existing.ImageData = artwork.ImageData;
            existing.UpdatedAtUtc = artwork.UpdatedAtUtc;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(long gameId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var artwork = await context.GameArtworks
            .FirstOrDefaultAsync(candidate => candidate.GameId == gameId, cancellationToken);
        if (artwork is null)
        {
            return;
        }

        context.GameArtworks.Remove(artwork);
        await context.SaveChangesAsync(cancellationToken);
    }
}
