using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Data.Repositories;

public sealed class NotificationLogRepository(IDbContextFactory<YFTimeTrackerDbContext> contextFactory) : INotificationLogRepository
{
    public event EventHandler? EntryAdded;

    public async Task<NotificationLogEntry> AddAsync(NotificationLogEntry entry, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.NotificationLogEntries.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
        EntryAdded?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public async Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.NotificationLogEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.NotificationLogEntries
            .AsNoTracking()
            .CountAsync(entry => !entry.IsRead, cancellationToken);
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.NotificationLogEntries
            .Where(entry => !entry.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.IsRead, true), cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.NotificationLogEntries
            .Where(entry => entry.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.NotificationLogEntries.ExecuteDeleteAsync(cancellationToken);
    }
}
