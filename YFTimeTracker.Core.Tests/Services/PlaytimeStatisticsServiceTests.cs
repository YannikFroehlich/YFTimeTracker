using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class PlaytimeStatisticsServiceTests
{
    [TestMethod]
    public async Task GetDashboardStatsAsync_returns_zero_values_for_empty_database()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        var sessions = new InMemoryGameSessionRepository(_ => null);
        var service = new PlaytimeStatisticsService(sessions, clock);

        var stats = await service.GetDashboardStatsAsync(TimeZoneInfo.Utc, CancellationToken.None);

        Assert.AreEqual(TimeSpan.Zero, stats.Today);
        Assert.AreEqual(TimeSpan.Zero, stats.PreviousDay);
        Assert.AreEqual(TimeSpan.Zero, stats.CurrentWeek);
        Assert.AreEqual(TimeSpan.Zero, stats.PreviousWeek);
        Assert.AreEqual(TimeSpan.Zero, stats.Total);
        Assert.AreEqual(0, stats.TodaySessionCount);
        Assert.AreEqual(0, stats.CurrentWeekSessionCount);
        Assert.AreEqual(0, stats.GamesPlayedCount);
        Assert.HasCount(7, stats.CurrentWeekDays);
        Assert.IsEmpty(stats.RunningGames);
        Assert.IsEmpty(stats.RecentGames);
    }

    [TestMethod]
    public async Task GetDashboardStatsAsync_aggregates_periods_days_and_recent_games()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        var games = new InMemoryGameRepository();
        var firstGame = await AddGameAsync(games, "Alpha", clock.UtcNow);
        var secondGame = await AddGameAsync(games, "Beta", clock.UtcNow);
        var sessions = new InMemoryGameSessionRepository(id =>
            id == firstGame.Id ? firstGame : id == secondGame.Id ? secondGame : null);

        await AddClosedSessionAsync(sessions, firstGame.Id, Utc(2026, 8, 17, 8), Utc(2026, 8, 17, 11));
        await AddClosedSessionAsync(sessions, firstGame.Id, Utc(2026, 8, 24, 8), Utc(2026, 8, 24, 10));
        await AddClosedSessionAsync(sessions, secondGame.Id, Utc(2026, 8, 25, 18), Utc(2026, 8, 25, 19));
        await AddClosedSessionAsync(sessions, firstGame.Id, Utc(2026, 8, 26, 8), Utc(2026, 8, 26, 9));
        await AddClosedSessionAsync(sessions, firstGame.Id, Utc(2026, 8, 27, 9), Utc(2026, 8, 27, 11));
        await sessions.AddAsync(new GameSession
        {
            GameId = firstGame.Id,
            StartedAtUtc = Utc(2026, 8, 27, 11, 30),
            LastSeenAtUtc = Utc(2026, 8, 27, 12),
            BootSessionId = "boot"
        }, CancellationToken.None);

        var service = new PlaytimeStatisticsService(sessions, clock);
        var stats = await service.GetDashboardStatsAsync(TimeZoneInfo.Utc, CancellationToken.None);

        Assert.AreEqual(TimeSpan.FromHours(2.5), stats.Today);
        Assert.AreEqual(TimeSpan.FromHours(1), stats.PreviousDay);
        Assert.AreEqual(TimeSpan.FromHours(6.5), stats.CurrentWeek);
        Assert.AreEqual(TimeSpan.FromHours(3), stats.PreviousWeek);
        Assert.AreEqual(TimeSpan.FromHours(9.5), stats.Total);
        Assert.AreEqual(2, stats.TodaySessionCount);
        Assert.AreEqual(5, stats.CurrentWeekSessionCount);
        Assert.AreEqual(2, stats.GamesPlayedCount);
        CollectionAssert.AreEqual(
            new[] { 2d, 1d, 1d, 2.5d, 0d, 0d, 0d },
            stats.CurrentWeekDays.Select(day => day.Duration.TotalHours).ToArray());

        Assert.HasCount(1, stats.RunningGames);
        Assert.AreEqual("Alpha", stats.RunningGames[0].Name);
        Assert.HasCount(2, stats.RecentGames);
        Assert.AreEqual("Alpha", stats.RecentGames[0].Name);
        Assert.AreEqual(TimeSpan.FromHours(8.5), stats.RecentGames[0].TotalDuration);
        Assert.IsTrue(stats.RecentGames[0].IsRunning);
        Assert.AreEqual("Beta", stats.RecentGames[1].Name);
        Assert.AreEqual(TimeSpan.FromHours(1), stats.RecentGames[1].TotalDuration);
        Assert.IsFalse(stats.RecentGames[1].IsRunning);
    }

    [TestMethod]
    public async Task GetDurationForLocalRangeAsync_splits_sessions_at_local_day_boundary()
    {
        var clock = new FakeClock(ToUtc(2026, 3, 29, 12, 0));
        var games = new InMemoryGameRepository();
        var game = await games.AddAsync(new Game
        {
            Name = "Test",
            ExecutablePath = @"C:\Games\Test.exe",
            ExecutablePathKey = @"C:\GAMES\TEST.EXE",
            ExecutableName = "Test.exe",
            AddedAtUtc = clock.UtcNow
        }, CancellationToken.None);

        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = ToUtc(2026, 3, 28, 23, 30),
            LastSeenAtUtc = ToUtc(2026, 3, 29, 1, 30),
            EndedAtUtc = ToUtc(2026, 3, 29, 1, 30),
            DurationSeconds = 7200,
            BootSessionId = "boot"
        }, CancellationToken.None);

        var service = new PlaytimeStatisticsService(sessions, clock);
        var duration = await service.GetDurationForLocalRangeAsync(
            new DateOnly(2026, 3, 29),
            new DateOnly(2026, 3, 30),
            TimeZoneInfo.Local,
            CancellationToken.None);

        Assert.AreEqual(TimeSpan.FromMinutes(90), duration);
    }

    [TestMethod]
    public async Task GetStatisticsAsync_aggregates_last_seven_days_previous_period_games_and_weekdays()
    {
        var clock = new FakeClock(Utc(2026, 8, 30, 12));
        var games = new InMemoryGameRepository();
        var alpha = await AddGameAsync(games, "Alpha", clock.UtcNow);
        var beta = await AddGameAsync(games, "Beta", clock.UtcNow);
        var sessions = new InMemoryGameSessionRepository(id => id == alpha.Id ? alpha : id == beta.Id ? beta : null);

        await AddClosedSessionAsync(sessions, alpha.Id, Utc(2026, 8, 20, 8), Utc(2026, 8, 20, 12));
        await AddClosedSessionAsync(sessions, alpha.Id, Utc(2026, 8, 24, 8), Utc(2026, 8, 24, 10));
        await AddClosedSessionAsync(sessions, beta.Id, Utc(2026, 8, 29, 15), Utc(2026, 8, 29, 19));
        await sessions.AddAsync(new GameSession
        {
            GameId = alpha.Id,
            StartedAtUtc = Utc(2026, 8, 30, 11),
            LastSeenAtUtc = clock.UtcNow,
            BootSessionId = "boot"
        }, CancellationToken.None);

        var service = new PlaytimeStatisticsService(sessions, clock);
        var report = await service.GetStatisticsAsync(
            StatisticsPeriodKind.Last7Days,
            TimeZoneInfo.Utc,
            CancellationToken.None);

        Assert.AreEqual(new DateOnly(2026, 8, 24), report.PeriodStart);
        Assert.AreEqual(new DateOnly(2026, 8, 31), report.PeriodEndExclusive);
        Assert.AreEqual(TimeSpan.FromHours(7), report.TotalDuration);
        Assert.AreEqual(TimeSpan.FromHours(4), report.PreviousPeriodDuration);
        Assert.AreEqual(3, report.SessionCount);
        Assert.AreEqual(2, report.GamesPlayedCount);
        Assert.AreEqual(TimeSpan.FromMinutes(140), report.AverageSessionDuration);
        Assert.AreEqual(TimeSpan.FromHours(4), report.LongestSessionDuration);
        Assert.AreEqual("Beta", report.LongestSessionGameName);
        CollectionAssert.AreEqual(
            new[] { 2d, 0d, 0d, 0d, 0d, 4d, 1d },
            report.Timeline.Select(point => point.Duration.TotalHours).ToArray());
        Assert.AreEqual(StatisticsBucketKind.Day, report.Timeline[0].BucketKind);
        Assert.AreEqual("Beta", report.Games[0].Name);
        Assert.AreEqual(TimeSpan.FromHours(4), report.Games[0].Duration);
        Assert.AreEqual(
            TimeSpan.FromHours(4),
            report.Weekdays.Single(day => day.DayOfWeek == DayOfWeek.Saturday).Duration);
        Assert.AreEqual(
            TimeSpan.FromHours(1),
            report.Weekdays.Single(day => day.DayOfWeek == DayOfWeek.Sunday).Duration);
    }

    [TestMethod]
    public async Task GetStatisticsAsync_builds_twelve_month_buckets_and_previous_year_comparison()
    {
        var clock = new FakeClock(Utc(2026, 8, 30, 12));
        var games = new InMemoryGameRepository();
        var game = await AddGameAsync(games, "Monatsspiel", clock.UtcNow);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);

        await AddClosedSessionAsync(sessions, game.Id, Utc(2024, 10, 15, 8), Utc(2024, 10, 15, 11));
        await AddClosedSessionAsync(sessions, game.Id, Utc(2025, 9, 10, 8), Utc(2025, 9, 10, 10));
        await AddClosedSessionAsync(sessions, game.Id, Utc(2026, 8, 10, 8), Utc(2026, 8, 10, 13));

        var service = new PlaytimeStatisticsService(sessions, clock);
        var report = await service.GetStatisticsAsync(
            StatisticsPeriodKind.Last12Months,
            TimeZoneInfo.Utc,
            CancellationToken.None);

        Assert.HasCount(12, report.Timeline);
        Assert.IsTrue(report.Timeline.All(point => point.BucketKind == StatisticsBucketKind.Month));
        Assert.AreEqual(new DateOnly(2025, 9, 1), report.Timeline[0].StartDate);
        Assert.AreEqual(TimeSpan.FromHours(2), report.Timeline[0].Duration);
        Assert.AreEqual(TimeSpan.FromHours(5), report.Timeline[^1].Duration);
        Assert.AreEqual(TimeSpan.FromHours(7), report.TotalDuration);
        Assert.AreEqual(TimeSpan.FromHours(3), report.PreviousPeriodDuration);
    }

    [TestMethod]
    public async Task GetStatisticsAsync_returns_stable_empty_all_time_report()
    {
        var clock = new FakeClock(Utc(2026, 8, 30, 12));
        var service = new PlaytimeStatisticsService(new InMemoryGameSessionRepository(_ => null), clock);

        var report = await service.GetStatisticsAsync(
            StatisticsPeriodKind.AllTime,
            TimeZoneInfo.Utc,
            CancellationToken.None);

        Assert.AreEqual(TimeSpan.Zero, report.TotalDuration);
        Assert.AreEqual(0, report.SessionCount);
        Assert.AreEqual(0, report.GamesPlayedCount);
        Assert.IsNull(report.PreviousPeriodDuration);
        Assert.HasCount(1, report.Timeline);
        Assert.HasCount(7, report.Weekdays);
        Assert.IsEmpty(report.Games);
    }

    [TestMethod]
    public void GetIsoWeekStart_returns_monday()
    {
        Assert.AreEqual(new DateOnly(2026, 8, 24), PlaytimeStatisticsService.GetIsoWeekStart(new DateOnly(2026, 8, 30)));
    }

    private static DateTimeOffset ToUtc(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0)
    {
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }

    private static Task<Game> AddGameAsync(InMemoryGameRepository games, string name, DateTimeOffset addedAtUtc)
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

    private static Task<GameSession> AddClosedSessionAsync(
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
}
