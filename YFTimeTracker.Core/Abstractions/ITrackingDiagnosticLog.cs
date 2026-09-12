using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface ITrackingDiagnosticLog
{
    IReadOnlyList<TrackingDiagnosticEvent> GetRecentEvents();

    void Record(
        TrackingDiagnosticEventKind kind,
        TrackingDiagnosticSeverity severity,
        string title,
        string detail);

    void Clear();
}
