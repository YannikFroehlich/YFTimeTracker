using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class SessionOverlapCalculatorTests
{
    [TestMethod]
    public void GetDurationForLocalRange_splits_sessions_at_local_day_boundary()
    {
        var sessions = new[]
        {
            new GameSession
            {
                GameId = 1,
                StartedAtUtc = ToUtc(2026, 3, 28, 23, 30),
                LastSeenAtUtc = ToUtc(2026, 3, 29, 1, 30),
                EndedAtUtc = ToUtc(2026, 3, 29, 1, 30),
                BootSessionId = "boot"
            }
        };

        var duration = SessionOverlapCalculator.GetDurationForLocalRange(
            sessions,
            new DateOnly(2026, 3, 29),
            new DateOnly(2026, 3, 30),
            TimeZoneInfo.Local,
            ToUtc(2026, 3, 29, 12, 0));

        Assert.AreEqual(TimeSpan.FromMinutes(90), duration);
    }

    [TestMethod]
    public void GetDurationForLocalRange_includes_open_session_up_to_now()
    {
        var now = Utc(2026, 8, 30, 12, 0);
        var sessions = new[]
        {
            new GameSession
            {
                GameId = 1,
                StartedAtUtc = Utc(2026, 8, 30, 11, 0),
                LastSeenAtUtc = now,
                BootSessionId = "boot"
            }
        };

        var duration = SessionOverlapCalculator.GetDurationForLocalRange(
            sessions,
            new DateOnly(2026, 8, 30),
            new DateOnly(2026, 8, 31),
            TimeZoneInfo.Utc,
            now);

        Assert.AreEqual(TimeSpan.FromHours(1), duration);
    }

    [TestMethod]
    public void GetDurationForLocalRange_ignores_sessions_of_other_games()
    {
        var now = Utc(2026, 8, 30, 12, 0);
        var sessions = new[]
        {
            new GameSession
            {
                GameId = 2,
                StartedAtUtc = Utc(2026, 8, 30, 8, 0),
                LastSeenAtUtc = Utc(2026, 8, 30, 9, 0),
                EndedAtUtc = Utc(2026, 8, 30, 9, 0),
                BootSessionId = "boot"
            }
        };

        var duration = SessionOverlapCalculator.GetDurationForLocalRange(
            sessions.Where(session => session.GameId == 1),
            new DateOnly(2026, 8, 30),
            new DateOnly(2026, 8, 31),
            TimeZoneInfo.Utc,
            now);

        Assert.AreEqual(TimeSpan.Zero, duration);
    }

    [TestMethod]
    public void GetDurationForLocalRange_returns_zero_for_empty_range()
    {
        var duration = SessionOverlapCalculator.GetDurationForLocalRange(
            [],
            new DateOnly(2026, 8, 30),
            new DateOnly(2026, 8, 30),
            TimeZoneInfo.Utc,
            Utc(2026, 8, 30, 12, 0));

        Assert.AreEqual(TimeSpan.Zero, duration);
    }

    private static DateTimeOffset ToUtc(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }
}
