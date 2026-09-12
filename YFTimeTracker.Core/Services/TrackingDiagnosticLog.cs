using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

public sealed class TrackingDiagnosticLog(IClock clock) : ITrackingDiagnosticLog
{
    private const int MaximumEventCount = 100;
    private readonly object sync = new();
    private readonly LinkedList<TrackingDiagnosticEvent> events = [];
    private long nextSequence;

    public IReadOnlyList<TrackingDiagnosticEvent> GetRecentEvents()
    {
        lock (sync)
        {
            return events.ToArray();
        }
    }

    public void Record(
        TrackingDiagnosticEventKind kind,
        TrackingDiagnosticSeverity severity,
        string title,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        var entry = new TrackingDiagnosticEvent(
            Interlocked.Increment(ref nextSequence),
            clock.UtcNow,
            kind,
            severity,
            title.Trim(),
            detail.Trim());

        lock (sync)
        {
            events.AddFirst(entry);
            while (events.Count > MaximumEventCount)
            {
                events.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (sync)
        {
            events.Clear();
        }
    }
}
