namespace YFTimeTracker.Core.Models;

public enum NotificationKind
{
    PlaytimeLimitReached,
    UpdateAvailable
}

public sealed class NotificationLogEntry
{
    public long Id { get; set; }

    public NotificationKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public bool IsRead { get; set; }

    public long? RelatedGameId { get; set; }
}
