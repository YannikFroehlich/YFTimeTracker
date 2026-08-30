using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class StatisticsViewModelTests
{
    [TestMethod]
    public async Task Refresh_formats_report_and_builds_chart_rankings_and_insights()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var report = CreateReport(
            total: TimeSpan.FromHours(8),
            previous: TimeSpan.FromHours(4),
            sessionCount: 3,
            games:
            [
                new GamePlaytimeStatistics(1, "Alpha Game", GameSource.Steam, TimeSpan.FromHours(6), 2, now),
                new GamePlaytimeStatistics(2, "Beta", GameSource.Manual, TimeSpan.FromHours(2), 1, now.AddDays(-1))
            ]);
        var viewModel = new StatisticsViewModel(new FakeStatisticsService(report), new FixedClock(now));

        await viewModel.RefreshAsync();

        Assert.AreEqual("8 h 00 min", viewModel.TotalDurationText);
        Assert.AreEqual("3 Sessions", viewModel.SessionCountText);
        Assert.AreEqual("2 h 40 min", viewModel.AverageDurationText);
        Assert.AreEqual("2 Spiele", viewModel.GamesPlayedText);
        Assert.AreEqual("+100 % zur Vorperiode", viewModel.ComparisonText);
        Assert.HasCount(7, viewModel.Timeline);
        Assert.HasCount(2, viewModel.TopGames);
        Assert.AreEqual("Alpha Game", viewModel.TopGames[0].Name);
        Assert.AreEqual("75 %", viewModel.TopGames[0].ShareText);
        Assert.AreEqual("Alpha Game", viewModel.TopGameText);
        Assert.AreEqual("Montag", viewModel.FavoriteWeekdayText);
        Assert.AreEqual("3 h 00 min", viewModel.LongestSessionText);
    }

    [TestMethod]
    public async Task Refresh_shows_real_empty_state_without_demo_values()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var report = CreateReport(TimeSpan.Zero, TimeSpan.Zero, 0, []);
        var viewModel = new StatisticsViewModel(new FakeStatisticsService(report), new FixedClock(now));

        await viewModel.RefreshAsync();

        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Visible, viewModel.EmptyVisibility);
        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.DataVisibility);
        Assert.AreEqual("0 min", viewModel.TotalDurationText);
        Assert.IsEmpty(viewModel.TopGames);
        Assert.AreEqual("Noch keine Daten", viewModel.TopGameText);
    }

    private static PlaytimeStatistics CreateReport(
        TimeSpan total,
        TimeSpan? previous,
        int sessionCount,
        IReadOnlyList<GamePlaytimeStatistics> games)
    {
        var start = new DateOnly(2026, 8, 24);
        return new PlaytimeStatistics(
            StatisticsPeriodKind.Last7Days,
            start,
            start.AddDays(7),
            total,
            previous,
            sessionCount,
            games.Count,
            sessionCount == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(total.Ticks / sessionCount),
            sessionCount == 0 ? TimeSpan.Zero : TimeSpan.FromHours(3),
            sessionCount == 0 ? null : "Alpha Game",
            Enumerable.Range(0, 7)
                .Select(offset => new StatisticsTimelinePoint(
                    start.AddDays(offset),
                    start.AddDays(offset + 1),
                    StatisticsBucketKind.Day,
                    offset == 0 ? total : TimeSpan.Zero))
                .ToArray(),
            games,
            Enumerable.Range(0, 7)
                .Select(offset => new WeekdayPlaytimeStatistics(
                    (DayOfWeek)(((int)DayOfWeek.Monday + offset) % 7),
                    offset == 0 ? total : TimeSpan.Zero))
                .ToArray());
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeStatisticsService(PlaytimeStatistics report) : IPlaytimeStatisticsService
    {
        public Task<DashboardStats> GetDashboardStatsAsync(TimeZoneInfo localTimeZone, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PlaytimeStatistics> GetStatisticsAsync(
            StatisticsPeriodKind period,
            TimeZoneInfo localTimeZone,
            CancellationToken cancellationToken) => Task.FromResult(report);

        public Task<TimeSpan> GetTotalDurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(report.TotalDuration);

        public Task<TimeSpan> GetDurationForLocalRangeAsync(
            DateOnly localStart,
            DateOnly localEndExclusive,
            TimeZoneInfo localTimeZone,
            CancellationToken cancellationToken) => Task.FromResult(report.TotalDuration);
    }
}
