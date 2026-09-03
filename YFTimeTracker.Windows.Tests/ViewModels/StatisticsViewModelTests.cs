using YFTimeTracker.App.Services;
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
        var viewModel = new StatisticsViewModel(new FakeStatisticsService(report), new FixedClock(now), new FakeFilePicker(), new FakeExplorerService());

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
        Assert.HasCount(7, viewModel.TrendLinePoints);
        Assert.HasCount(2, viewModel.GameShares);
        Assert.AreEqual("Alpha Game", viewModel.GameShares[0].Name);
        Assert.AreEqual("75 %", viewModel.GameShares[0].ShareText);
        Assert.HasCount(26, viewModel.HeatmapWeeks);
        Assert.HasCount(7, viewModel.HeatmapWeeks[0].Days);
    }

    [TestMethod]
    public async Task Refresh_shows_real_empty_state_without_demo_values()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var report = CreateReport(TimeSpan.Zero, TimeSpan.Zero, 0, []);
        var viewModel = new StatisticsViewModel(new FakeStatisticsService(report), new FixedClock(now), new FakeFilePicker(), new FakeExplorerService());

        await viewModel.RefreshAsync();

        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Visible, viewModel.EmptyVisibility);
        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.DataVisibility);
        Assert.AreEqual("0 min", viewModel.TotalDurationText);
        Assert.IsEmpty(viewModel.TopGames);
        Assert.AreEqual("Noch keine Daten", viewModel.TopGameText);
        Assert.IsEmpty(viewModel.GameShares);
        Assert.HasCount(26, viewModel.HeatmapWeeks);
    }

    [TestMethod]
    public async Task ExportCsv_writes_games_table_to_picked_path()
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
        var exportPath = Path.Combine(Path.GetTempPath(), $"yftimetracker-statistics-test-{Guid.NewGuid():N}.csv");
        var filePicker = new FakeFilePicker(exportPath);
        var explorerService = new FakeExplorerService();
        var viewModel = new StatisticsViewModel(new FakeStatisticsService(report), new FixedClock(now), filePicker, explorerService);
        await viewModel.RefreshAsync();

        try
        {
            Assert.IsTrue(viewModel.IsExportEnabled);

            await viewModel.ExportCsvCommand.ExecuteAsync(null);

            var lines = await File.ReadAllLinesAsync(exportPath);
            var alphaLastPlayed = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).ToString("dd.MM.yyyy");
            var betaLastPlayed = TimeZoneInfo.ConvertTime(now.AddDays(-1), TimeZoneInfo.Local).ToString("dd.MM.yyyy");
            Assert.AreEqual("Rang;Spiel;Quelle;Spielzeit;Spielzeit (Stunden);Anteil (%);Sessions;Zuletzt gespielt", lines[0]);
            Assert.AreEqual($"1;Alpha Game;STEAM;6 h 00 min;6,00;75,0;2;{alphaLastPlayed}", lines[1]);
            Assert.AreEqual($"2;Beta;MANUELL;2 h 00 min;2,00;25,0;1;{betaLastPlayed}", lines[2]);
            StringAssert.Contains(viewModel.StatusMessage, Path.GetFileName(exportPath));
            Assert.IsTrue(viewModel.IsExportFolderAvailable);

            viewModel.OpenExportFolderCommand.Execute(null);
            Assert.AreEqual(exportPath, explorerService.RevealedPath);
        }
        finally
        {
            File.Delete(exportPath);
        }
    }

    [TestMethod]
    public async Task ExportCsv_is_disabled_without_games()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var report = CreateReport(TimeSpan.Zero, TimeSpan.Zero, 0, []);
        var filePicker = new FakeFilePicker(Path.Combine(Path.GetTempPath(), "should-not-be-created.csv"));
        var explorerService = new FakeExplorerService();
        var viewModel = new StatisticsViewModel(new FakeStatisticsService(report), new FixedClock(now), filePicker, explorerService);
        await viewModel.RefreshAsync();

        await viewModel.ExportCsvCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.IsExportEnabled);
        Assert.IsFalse(viewModel.IsExportFolderAvailable);
        Assert.IsFalse(File.Exists(Path.Combine(Path.GetTempPath(), "should-not-be-created.csv")));
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

        public Task<TimeSpan> GetDurationForGameAndLocalRangeAsync(
            long gameId,
            DateOnly localStart,
            DateOnly localEndExclusive,
            TimeZoneInfo localTimeZone,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DailyPlaytimeInfo>> GetActivityHeatmapAsync(
            int weekCount,
            TimeZoneInfo localTimeZone,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DailyPlaytimeInfo>>(
                Enumerable.Range(0, weekCount * 7)
                    .Select(offset => new DailyPlaytimeInfo(
                        new DateOnly(2026, 8, 30).AddDays(offset - (weekCount * 7 - 1)),
                        offset == weekCount * 7 - 1 ? TimeSpan.FromMinutes(45) : TimeSpan.Zero))
                    .ToArray());
    }

    private sealed class FakeFilePicker(string? exportPath = null) : IFilePickerService
    {
        public Task<string?> PickExecutableAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickExportArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickDiagnosticsArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickImportArchiveAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickYearReviewImageAsync(int year, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<string?> PickStatisticsExportAsync(string periodLabel, CancellationToken cancellationToken) => Task.FromResult(exportPath);

        public Task<string?> PickSessionsExportAsync(string periodLabel, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class FakeExplorerService : IExplorerService
    {
        public string? RevealedPath { get; private set; }

        public void RevealFile(string path) => RevealedPath = path;
    }
}
