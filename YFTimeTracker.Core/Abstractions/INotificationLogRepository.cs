using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface INotificationLogRepository
{
    Task<NotificationLogEntry> AddAsync(NotificationLogEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken);

    Task MarkAllAsReadAsync(CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    Task ClearAllAsync(CancellationToken cancellationToken);
}
