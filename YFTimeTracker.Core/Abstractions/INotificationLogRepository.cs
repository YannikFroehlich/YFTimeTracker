using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface INotificationLogRepository
{
    /// <summary>
    /// Wird ausgelöst, wenn ein Eintrag hinzugefügt oder im Hintergrund entfernt wurde
    /// (<see cref="AddAsync"/>, <see cref="DeleteByKindAsync"/>).
    /// </summary>
    event EventHandler? Changed;

    Task<NotificationLogEntry> AddAsync(NotificationLogEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(NotificationKind kind, string referenceKey, CancellationToken cancellationToken);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken);

    Task MarkAllAsReadAsync(CancellationToken cancellationToken);

    Task DeleteAsync(long id, CancellationToken cancellationToken);

    Task DeleteByKindAsync(NotificationKind kind, CancellationToken cancellationToken);

    Task ClearAllAsync(CancellationToken cancellationToken);
}
