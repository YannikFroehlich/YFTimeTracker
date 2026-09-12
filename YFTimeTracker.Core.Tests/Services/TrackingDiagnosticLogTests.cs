using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class TrackingDiagnosticLogTests
{
    [TestMethod]
    public void Record_keeps_newest_one_hundred_events_in_reverse_order()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-09-12T10:00:00Z"));
        var log = new TrackingDiagnosticLog(clock);

        for (var index = 1; index <= 105; index++)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(1);
            log.Record(
                TrackingDiagnosticEventKind.Status,
                TrackingDiagnosticSeverity.Information,
                $"Ereignis {index}",
                "Testdetail");
        }

        var events = log.GetRecentEvents();
        Assert.HasCount(100, events);
        Assert.AreEqual("Ereignis 105", events[0].Title);
        Assert.AreEqual("Ereignis 6", events[^1].Title);
        Assert.IsTrue(events.Zip(events.Skip(1)).All(pair => pair.First.Sequence > pair.Second.Sequence));
    }

    [TestMethod]
    public void Clear_removes_all_events()
    {
        var log = new TrackingDiagnosticLog(new FakeClock(DateTimeOffset.Parse("2026-09-12T10:00:00Z")));
        log.Record(
            TrackingDiagnosticEventKind.Status,
            TrackingDiagnosticSeverity.Success,
            "Tracking gestartet",
            "Testdetail");

        log.Clear();

        Assert.IsEmpty(log.GetRecentEvents());
    }
}
