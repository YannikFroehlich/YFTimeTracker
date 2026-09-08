namespace YFTimeTracker.Core.Models;

public sealed record GlobalSearchQuery(
    string SearchText,
    GameSource? Source = null,
    DateTimeOffset? SessionsSinceUtc = null);
