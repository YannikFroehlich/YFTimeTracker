namespace YFTimeTracker.Core.Models;

public sealed class TrackingExclusionRule
{
    public long Id { get; set; }

    public TrackingExclusionKind Kind { get; set; }

    public string Value { get; set; } = string.Empty;

    public string ValueKey { get; set; } = string.Empty;

    public DateTimeOffset AddedAtUtc { get; set; }
}

public enum TrackingExclusionKind
{
    Executable,
    Directory
}
