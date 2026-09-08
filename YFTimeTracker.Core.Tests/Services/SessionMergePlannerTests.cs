using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class SessionMergePlannerTests
{
    [TestMethod]
    public void Sessions_that_do_not_overlap_only_change_their_game()
    {
        var target = new[] { Session(1, "10:00", "11:00") };
        var source = new[] { Session(2, "12:00", "13:00") };

        var plan = SessionMergePlanner.Create(target, source);

        // Die Session des Zielspiels bleibt unberührt und wird deshalb nicht neu geschrieben.
        Assert.HasCount(1, plan.Updates);
        Assert.AreEqual(2, plan.Updates[0].SessionId);
        Assert.IsEmpty(plan.RemovedSessionIds);
    }

    [TestMethod]
    public void Overlapping_sessions_become_one_spanning_the_whole_range()
    {
        var target = new[] { Session(1, "10:00", "11:30") };
        var source = new[] { Session(2, "11:00", "12:00") };

        var plan = SessionMergePlanner.Create(target, source);

        var survivor = plan.Updates.Single();
        Assert.AreEqual(1, survivor.SessionId);
        Assert.AreEqual(At("10:00"), survivor.StartedAtUtc);
        Assert.AreEqual(At("12:00"), survivor.EndedAtUtc);
        Assert.AreEqual(2, plan.RemovedSessionIds.Single());
    }

    [TestMethod]
    public void A_session_inside_another_one_is_absorbed_without_rewriting_the_survivor()
    {
        var target = new[] { Session(1, "10:00", "14:00") };
        var source = new[] { Session(2, "11:00", "12:00") };

        var plan = SessionMergePlanner.Create(target, source);

        Assert.IsEmpty(plan.Updates);
        Assert.AreEqual(2, plan.RemovedSessionIds.Single());
    }

    [TestMethod]
    public void Sessions_that_only_touch_stay_separate()
    {
        var target = new[] { Session(1, "10:00", "11:00") };
        var source = new[] { Session(2, "11:00", "12:00") };

        var plan = SessionMergePlanner.Create(target, source);

        // Gleiche Regel wie HasOverlapAsync: Ende == Start ist keine Überschneidung.
        Assert.AreEqual(2, plan.Updates.Single().SessionId);
        Assert.IsEmpty(plan.RemovedSessionIds);
    }

    [TestMethod]
    public void A_chain_of_overlaps_collapses_into_a_single_session()
    {
        var target = new[] { Session(1, "10:00", "11:00"), Session(3, "11:30", "13:00") };
        var source = new[] { Session(2, "10:30", "12:00") };

        var plan = SessionMergePlanner.Create(target, source);

        // 1 überlappt 2, 2 überlappt 3, 1 und 3 aber nicht - trotzdem ist es eine Spielzeit.
        var survivor = plan.Updates.Single();
        Assert.AreEqual(1, survivor.SessionId);
        Assert.AreEqual(At("10:00"), survivor.StartedAtUtc);
        Assert.AreEqual(At("13:00"), survivor.EndedAtUtc);
        CollectionAssert.AreEquivalent(new long[] { 2, 3 }, plan.RemovedSessionIds.ToArray());
    }

    [TestMethod]
    public void Merging_never_reports_more_playtime_than_before()
    {
        var target = new[] { Session(1, "10:00", "11:30"), Session(3, "15:00", "16:00") };
        var source = new[] { Session(2, "11:00", "12:00"), Session(4, "20:00", "21:00") };

        var plan = SessionMergePlanner.Create(target, source);

        var before = target.Concat(source).Sum(session => (session.EndedAtUtc!.Value - session.StartedAtUtc).Ticks);
        var removed = plan.RemovedSessionIds.ToHashSet();
        var after = target.Concat(source)
            .Where(session => !removed.Contains(session.Id))
            .Sum(session => plan.Updates.FirstOrDefault(update => update.SessionId == session.Id) is { } update
                ? (update.EndedAtUtc - update.StartedAtUtc).Ticks
                : (session.EndedAtUtc!.Value - session.StartedAtUtc).Ticks);

        Assert.IsLessThan(before, after, "Zusammenfassen darf Spielzeit nie erhöhen.");
        Assert.AreEqual(TimeSpan.FromHours(4).Ticks, after);
    }

    private static GameSession Session(long id, string start, string end) => new()
    {
        Id = id,
        GameId = 99,
        StartedAtUtc = At(start),
        LastSeenAtUtc = At(end),
        EndedAtUtc = At(end),
        DurationSeconds = (long)(At(end) - At(start)).TotalSeconds,
        BootSessionId = "boot"
    };

    private static DateTimeOffset At(string time) => DateTimeOffset.Parse($"2026-09-08T{time}:00Z");
}
