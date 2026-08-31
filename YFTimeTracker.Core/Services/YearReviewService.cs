using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

public sealed class YearReviewService(
    IGameSessionRepository sessions,
    IPlaytimeReadRepository readRepository,
    IClock clock) : IYearReviewService
{
    public async Task<IReadOnlyList<int>> GetAvailableYearsAsync(
        TimeZoneInfo localTimeZone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(localTimeZone);
        var currentYear = TimeZoneInfo.ConvertTime(clock.UtcNow, localTimeZone).Year;
        var earliestSessionStartUtc = await readRepository.GetEarliestSessionStartAsync(cancellationToken);
        var earliestYear = Math.Max(2, earliestSessionStartUtc is null
            ? currentYear
            : Math.Min(
                currentYear,
                TimeZoneInfo.ConvertTime(earliestSessionStartUtc.Value, localTimeZone).Year));

        return Enumerable.Range(earliestYear, currentYear - earliestYear + 1)
            .Reverse()
            .ToArray();
    }

    public async Task<YearReview> GetYearReviewAsync(
        int year,
        TimeZoneInfo localTimeZone,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9998);
        ArgumentNullException.ThrowIfNull(localTimeZone);

        var previousYearStart = new DateOnly(year - 1, 1, 1);
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year + 1, 1, 1);
        var previousYearStartUtc = LocalDateStartToUtc(previousYearStart, localTimeZone);
        var yearStartUtc = LocalDateStartToUtc(yearStart, localTimeZone);
        var yearEndUtc = LocalDateStartToUtc(yearEnd, localTimeZone);
        var relevantSessions = await sessions.GetSessionsAsync(
            previousYearStartUtc,
            yearEndUtc,
            cancellationToken);

        var currentContributions = relevantSessions
            .Select(session => new SessionContribution(
                session,
                GetOverlap(session, yearStartUtc, yearEndUtc)))
            .Where(contribution => contribution.Duration > TimeSpan.Zero)
            .ToArray();
        var totalDuration = TimeSpan.FromTicks(currentContributions.Sum(item => item.Duration.Ticks));
        var previousYearDuration = GetDurationForRange(
            relevantSessions,
            previousYearStartUtc,
            yearStartUtc);

        var months = Enumerable.Range(1, 12)
            .Select(month =>
            {
                var monthStart = new DateOnly(year, month, 1);
                var monthEnd = monthStart.AddMonths(1);
                return new YearReviewMonth(
                    month,
                    GetDurationForRange(
                        relevantSessions,
                        LocalDateStartToUtc(monthStart, localTimeZone),
                        LocalDateStartToUtc(monthEnd, localTimeZone)));
            })
            .ToArray();

        var games = currentContributions
            .GroupBy(contribution => contribution.Session.GameId)
            .Select(group =>
            {
                var game = group
                    .Select(contribution => contribution.Session.Game)
                    .FirstOrDefault(candidate => candidate is not null);
                return new YearReviewGame(
                    group.Key,
                    game?.Name ?? "Unbekanntes Spiel",
                    game?.Source ?? GameSource.Manual,
                    TimeSpan.FromTicks(group.Sum(contribution => contribution.Duration.Ticks)),
                    group.Count(),
                    game?.PrimaryExecutable?.ExecutablePath);
            })
            .OrderByDescending(game => game.Duration)
            .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var longest = currentContributions
            .OrderByDescending(contribution => contribution.Duration)
            .ThenBy(contribution => contribution.Session.StartedAtUtc)
            .FirstOrDefault();
        var activeDays = GetActiveDays(
            currentContributions,
            yearStartUtc,
            yearEndUtc,
            localTimeZone);
        var mostActiveMonth = months
            .Where(month => month.Duration > TimeSpan.Zero)
            .OrderByDescending(month => month.Duration)
            .ThenBy(month => month.Month)
            .FirstOrDefault();

        return new YearReview(
            year,
            totalDuration,
            previousYearDuration,
            currentContributions.Length,
            games.Length,
            activeDays.Count,
            longest?.Duration ?? TimeSpan.Zero,
            longest?.Session.Game?.Name,
            longest is null
                ? null
                : Max(longest.Session.StartedAtUtc, yearStartUtc),
            mostActiveMonth,
            months,
            games);
    }

    private HashSet<DateOnly> GetActiveDays(
        IReadOnlyList<SessionContribution> contributions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        TimeZoneInfo localTimeZone)
    {
        var activeDays = new HashSet<DateOnly>();
        foreach (var contribution in contributions)
        {
            var overlapStart = Max(contribution.Session.StartedAtUtc, rangeStartUtc);
            var overlapEnd = Min(GetEffectiveSessionEnd(contribution.Session), rangeEndUtc);
            var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(overlapStart, localTimeZone).Date);
            var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(overlapEnd.AddTicks(-1), localTimeZone).Date);

            for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            {
                if (GetOverlap(
                        contribution.Session,
                        LocalDateStartToUtc(date, localTimeZone),
                        LocalDateStartToUtc(date.AddDays(1), localTimeZone)) > TimeSpan.Zero)
                {
                    activeDays.Add(date);
                }
            }
        }

        return activeDays;
    }

    private TimeSpan GetDurationForRange(
        IEnumerable<GameSession> relevantSessions,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        return TimeSpan.FromTicks(relevantSessions.Sum(session =>
            GetOverlap(session, rangeStartUtc, rangeEndUtc).Ticks));
    }

    private TimeSpan GetOverlap(
        GameSession session,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        var overlapStart = Max(session.StartedAtUtc, rangeStartUtc);
        var overlapEnd = Min(GetEffectiveSessionEnd(session), rangeEndUtc);
        return overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
    }

    private DateTimeOffset GetEffectiveSessionEnd(GameSession session) => session.EndedAtUtc ?? clock.UtcNow;

    private static DateTimeOffset LocalDateStartToUtc(DateOnly localDate, TimeZoneInfo localTimeZone)
    {
        var localDateTime = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = localTimeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private sealed record SessionContribution(GameSession Session, TimeSpan Duration);
}
