using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class YearReviewViewModelTests
{
    [TestMethod]
    public async Task Initialize_formats_summary_months_highlights_and_top_games()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var months = Enumerable.Range(1, 12)
            .Select(month => new YearReviewMonth(
                month,
                month == 8 ? TimeSpan.FromHours(8) : month == 2 ? TimeSpan.FromHours(2) : TimeSpan.Zero))
            .ToArray();
        var report = new YearReview(
            2026,
            TimeSpan.FromHours(10),
            TimeSpan.FromHours(5),
            4,
            2,
            3,
            TimeSpan.FromHours(3),
            "Alpha Game",
            now.AddDays(-5),
            months[7],
            months,
            [
                new YearReviewGame(1, "Alpha Game", GameSource.Steam, TimeSpan.FromHours(6), 3, @"C:\Games\Alpha.exe"),
                new YearReviewGame(2, "Beta", GameSource.Manual, TimeSpan.FromHours(4), 1, @"C:\Games\Beta.exe")
            ]);
        var iconService = new FakeGameIconService(@"C:\Cache\game.png");
        var viewModel = new YearReviewViewModel(
            new FakeYearReviewService([2026, 2025], report),
            new FixedClock(now),
            iconService);

        await viewModel.InitializeAsync();

        Assert.AreEqual("DEIN JAHR 2026", viewModel.YearTitle);
        Assert.AreEqual("10 h 00 min", viewModel.TotalPlaytimeText);
        Assert.AreEqual("+100 % gegenüber 2025", viewModel.ComparisonText);
        Assert.AreEqual("3 Tage", viewModel.ActiveDaysText);
        Assert.AreEqual("2 Spiele", viewModel.GamesPlayedText);
        Assert.AreEqual("4 Sessions", viewModel.SessionCountText);
        Assert.AreEqual("August", viewModel.MostActiveMonthText);
        Assert.AreEqual("8 h 00 min Spielzeit", viewModel.MostActiveMonthDetailText);
        Assert.AreEqual("3 h 00 min", viewModel.LongestSessionText);
        StringAssert.Contains(viewModel.LongestSessionDetailText, "Alpha Game");
        Assert.HasCount(12, viewModel.Months);
        Assert.AreEqual(156d, viewModel.Months[7].BarHeight);
        Assert.HasCount(2, viewModel.TopGames);
        Assert.AreEqual("Alpha Game", viewModel.TopGames[0].Name);
        Assert.AreEqual("60 %", viewModel.TopGames[0].ShareText);
        Assert.AreEqual(@"C:\Cache\game.png", viewModel.TopGames[0].IconPath);
        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Visible, viewModel.DataVisibility);
        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.EmptyVisibility);
        Assert.AreEqual(2, iconService.CallCount);
    }

    [TestMethod]
    public async Task Initialize_shows_real_empty_state_for_year_without_sessions()
    {
        var now = DateTimeOffset.Parse("2026-08-31T12:00:00Z");
        var report = new YearReview(
            2026,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            0,
            0,
            TimeSpan.Zero,
            null,
            null,
            null,
            Enumerable.Range(1, 12).Select(month => new YearReviewMonth(month, TimeSpan.Zero)).ToArray(),
            []);
        var viewModel = new YearReviewViewModel(
            new FakeYearReviewService([2026], report),
            new FixedClock(now));

        await viewModel.InitializeAsync();

        Assert.AreEqual("0 min", viewModel.TotalPlaytimeText);
        Assert.AreEqual("Noch kein aktiver Monat", viewModel.MostActiveMonthText);
        Assert.AreEqual("Noch keine Session", viewModel.LongestSessionText);
        Assert.IsEmpty(viewModel.TopGames);
        Assert.HasCount(12, viewModel.Months);
        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Collapsed, viewModel.DataVisibility);
        Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Visible, viewModel.EmptyVisibility);
    }

    private sealed class FakeYearReviewService(
        IReadOnlyList<int> years,
        YearReview report) : IYearReviewService
    {
        public Task<IReadOnlyList<int>> GetAvailableYearsAsync(
            TimeZoneInfo localTimeZone,
            CancellationToken cancellationToken) => Task.FromResult(years);

        public Task<YearReview> GetYearReviewAsync(
            int year,
            TimeZoneInfo localTimeZone,
            CancellationToken cancellationToken) => Task.FromResult(report with { Year = year });
    }

    private sealed class FakeGameIconService(string iconPath) : IGameIconService
    {
        public int CallCount { get; private set; }

        public Task<string?> GetIconPathAsync(string? executablePath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<string?>(iconPath);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
