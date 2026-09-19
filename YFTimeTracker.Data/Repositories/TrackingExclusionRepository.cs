using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Data.Sync;

namespace YFTimeTracker.Data.Repositories;

public sealed class TrackingExclusionRepository(
    IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : ITrackingExclusionRepository
{
    public async Task<IReadOnlyList<TrackingExclusionRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.TrackingExclusionRules
            .AsNoTracking()
            .OrderBy(rule => rule.Kind)
            .ThenBy(rule => rule.Value)
            .ToListAsync(cancellationToken);
    }

    public async Task<TrackingExclusionRule> AddAsync(
        TrackingExclusionRule rule,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.TrackingExclusionRules.Add(rule);
        await context.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await context.TrackingExclusionRules
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (rule is null)
        {
            return;
        }

        SyncTombstoneRecorder.RecordExclusion(context, rule);
        context.TrackingExclusionRules.Remove(rule);
        await context.SaveChangesAsync(cancellationToken);
    }
}
