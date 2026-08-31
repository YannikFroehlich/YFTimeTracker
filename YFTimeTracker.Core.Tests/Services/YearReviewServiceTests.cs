using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class YearReviewServiceTests
{
    [TestMethod]
    public async Task Review_aggregates_months_days_games_records_and_previous_year()
    {
        var clock = new FakeClock(Utc(2026, 8, 31, 12));
        var games = new InMemoryGameRepository();
        var alpha = await AddGameAsync(games, "Alpha", clock.UtcNow);
        var beta = await AddGameAsync(games, "Beta", clock.UtcNow);
        var sessions = new InMemoryGameSessionRepository(id =>
            id == alpha.Id ? alpha : id == beta.Id ? beta : null);

        await AddSessionAsync(sessions, alpha.Id, Utc(2025, 6, 1, 10), Utc(2025, 6, 1, 15));
        await AddSessionAsync(sessions, alpha.Id, Utc(2025, 12, 31, 23), Utc(2026, 1, 1, 1));
        await AddSessionAsync(sessions, alpha.Id, Utc(2026, 1, 1, 22), Utc(2026, 1, 2, 1));
        await AddSessionAsync(sessions, beta.Id, Utc(2026, 3, 10, 10), Utc(2026, 3, 10, 17));
        await AddSessionAsync(sessions, alpha.Id, Utc(2026, 3, 11, 18), Utc(2026, 3, 11, 20));

        var service = new YearReviewService(
            sessions,
            new InMemoryPlaytimeReadRepository(sessions),
            clock);

        var review = await service.GetYearReviewAsync(2026, TimeZoneInfo.Utc, CancellationToken.None);

        Assert.AreEqual(TimeSpan.FromHours(13), review.TotalDuration);
        Assert.AreEqual(TimeSpan.FromHours(6), review.PreviousYearDuration);
        Assert.AreEqual(4, review.SessionCount);
        Assert.AreEqual(2, review.GamesPlayedCount);
        Assert.AreEqual(4, review.ActiveDayCount);
        Assert.AreEqual(TimeSpan.FromHours(7), review.LongestSessionDuration);
        Assert.AreEqual("Beta", review.LongestSessionGameName);
        Assert.AreEqual(Utc(2026, 3, 10, 10), review.LongestSessionStartedAtUtc);
        Assert.AreEqual(3, review.MostActiveMonth?.Month);
        Assert.AreEqual(TimeSpan.FromHours(9), review.MostActiveMonth?.Duration);
        Assert.HasCount(12, review.Months);
        Assert.AreEqual(TimeSpan.FromHours(4), review.Months.Single(month => month.Month == 1).Duration);
        Assert.AreEqual(TimeSpan.FromHours(9), review.Months.Single(month => month.Month == 3).Duration);
        Assert.AreEqual("Beta", review.Games[0].Name);
        Assert.AreEqual(TimeSpan.FromHours(7), review.Games[0].Duration);
        Assert.AreEqual(@"C:\Games\Beta.exe", review.Games[0].ExecutablePath);
        Assert.AreEqual(Utc(2025, 1, 1, 0), sessions.LastQueryFromUtc);
        Assert.AreEqual(Utc(2027, 1, 1, 0), sessions.LastQueryToUtc);
    }

    [TestMethod]
    public async Task Available_years_span_earliest_session_through_current_year()
    {
        var clock = new FakeClock(Utc(2026, 8, 31, 12));
        var games = new InMemoryGameRepository();
        var game = await AddGameAsync(games, "Alpha", clock.UtcNow);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        await AddSessionAsync(sessions, game.Id, Utc(2023, 4, 2, 10), Utc(2023, 4, 2, 11));
        var service = new YearReviewService(
            sessions,
            new InMemoryPlaytimeReadRepository(sessions),
            clock);

        var years = await service.GetAvailableYearsAsync(TimeZoneInfo.Utc, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 2026, 2025, 2024, 2023 }, years.ToArray());
    }

    [TestMethod]
    public async Task Empty_review_contains_twelve_zero_months()
    {
        var clock = new FakeClock(Utc(2026, 8, 31, 12));
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var service = new YearReviewService(
            sessions,
            new InMemoryPlaytimeReadRepository(sessions),
            clock);

        var review = await service.GetYearReviewAsync(2026, TimeZoneInfo.Utc, CancellationToken.None);

        Assert.AreEqual(TimeSpan.Zero, review.TotalDuration);
        Assert.AreEqual(TimeSpan.Zero, review.PreviousYearDuration);
        Assert.AreEqual(0, review.ActiveDayCount);
        Assert.IsNull(review.MostActiveMonth);
        Assert.IsNull(review.LongestSessionGameName);
        Assert.HasCount(12, review.Months);
        Assert.IsTrue(review.Months.All(month => month.Duration == TimeSpan.Zero));
        Assert.IsEmpty(review.Games);
    }

    private static Task<Game> AddGameAsync(
        InMemoryGameRepository games,
        string name,
        DateTimeOffset addedAtUtc)
    {
        return games.AddAsync(new Game
        {
            Name = name,
            ExecutablePath = $@"C:\Games\{name}.exe",
            ExecutablePathKey = $@"C:\GAMES\{name.ToUpperInvariant()}.EXE",
            ExecutableName = $"{name}.exe",
            AddedAtUtc = addedAtUtc
        }, CancellationToken.None);
    }

    private static Task<GameSession> AddSessionAsync(
        InMemoryGameSessionRepository sessions,
        long gameId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc)
    {
        return sessions.AddAsync(new GameSession
        {
            GameId = gameId,
            StartedAtUtc = startedAtUtc,
            LastSeenAtUtc = endedAtUtc,
            EndedAtUtc = endedAtUtc,
            DurationSeconds = Convert.ToInt64((endedAtUtc - startedAtUtc).TotalSeconds),
            BootSessionId = "boot"
        }, CancellationToken.None);
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
