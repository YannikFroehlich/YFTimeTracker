using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class NotificationListItemViewModel
{
    public NotificationKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string TimestampText { get; set; } = string.Empty;

    public long? RelatedGameId { get; set; }
}
