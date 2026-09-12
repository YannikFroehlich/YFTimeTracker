namespace YFTimeTracker.Core.Models;

public enum TrackingDiagnosticEventKind
{
    Status,
    Detection,
    Assignment,
    Exclusion,
    Session,
    Interruption,
    Error
}

public enum TrackingDiagnosticSeverity
{
    Information,
    Success,
    Warning,
    Error
}

public sealed record TrackingDiagnosticEvent(
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    TrackingDiagnosticEventKind Kind,
    TrackingDiagnosticSeverity Severity,
    string Title,
    string Detail);
