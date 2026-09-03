using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

public static class SessionOverlapCalculator
{
    public static DateTimeOffset LocalDateStartToUtc(DateOnly localDate, TimeZoneInfo localTimeZone)
    {
        var localDateTime = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = localTimeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }

    public static TimeSpan GetDurationForLocalRange(
        IEnumerable<GameSession> sessions,
        DateOnly localStart,
        DateOnly localEndExclusive,
        TimeZoneInfo localTimeZone,
        DateTimeOffset nowUtc)
    {
        if (localEndExclusive <= localStart)
        {
            return TimeSpan.Zero;
        }

        var rangeStartUtc = LocalDateStartToUtc(localStart, localTimeZone);
        var rangeEndUtc = LocalDateStartToUtc(localEndExclusive, localTimeZone);
        return GetDurationForUtcRange(sessions, rangeStartUtc, rangeEndUtc, nowUtc);
    }

    public static TimeSpan GetDurationForUtcRange(
        IEnumerable<GameSession> sessions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        DateTimeOffset nowUtc)
    {
        var total = TimeSpan.Zero;
        foreach (var session in sessions)
        {
            total += GetOverlap(session, rangeStartUtc, rangeEndUtc, nowUtc);
        }

        return total;
    }

    private static TimeSpan GetOverlap(GameSession session, DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc, DateTimeOffset nowUtc)
    {
        var sessionEnd = session.EndedAtUtc ?? nowUtc;
        var overlapStart = session.StartedAtUtc > rangeStartUtc ? session.StartedAtUtc : rangeStartUtc;
        var overlapEnd = sessionEnd < rangeEndUtc ? sessionEnd : rangeEndUtc;
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
    }
}
