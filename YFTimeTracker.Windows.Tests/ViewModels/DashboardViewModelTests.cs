using System.Collections.Specialized;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class DashboardViewModelTests
{
    [TestMethod]
    public async Task Five_second_refresh_keeps_existing_chart_and_recent_game_controls()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var statistics = new FakeStatisticsService(CreateStats(now, TimeSpan.Zero));
        var viewModel = new DashboardViewModel(
            statistics,
            new FakeTrackingService(),
            new FixedClock(now));

        await viewModel.RefreshAsync();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).Date);
        var weekStart = YFTimeTracker.Core.Services.PlaytimeStatisticsService.GetIsoWeekStart(today);
        var originalDay = viewModel.WeekDays[today.DayNumber - weekStart.DayNumber];
        var originalGame = viewModel.RecentGames[0];
        var collectionChanges = 0;
        var dayPropertyChanges = 0;
        var gamePropertyChanges = 0;
        viewModel.WeekDays.CollectionChanged += CountCollectionChange;
        viewModel.RecentGames.CollectionChanged += CountCollectionChange;
        originalDay.PropertyChanged += (_, _) => dayPropertyChanges++;
        originalGame.PropertyChanged += (_, _) => gamePropertyChanges++;

        statistics.Current = CreateStats(now, TimeSpan.FromSeconds(5));
        await viewModel.RefreshAsync();

        Assert.AreSame(originalDay, viewModel.WeekDays[today.DayNumber - weekStart.DayNumber]);
        Assert.AreSame(originalGame, viewModel.RecentGames[0]);
        Assert.AreEqual(0, collectionChanges);
        Assert.AreEqual(0, dayPropertyChanges);
        Assert.AreEqual(0, gamePropertyChanges);

        void CountCollectionChange(object? sender, NotifyCollectionChangedEventArgs args) => collectionChanges++;
    }

    private static DashboardStats CreateStats(DateTimeOffset now, TimeSpan liveIncrease)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).Date);
        var weekStart = YFTimeTracker.Core.Services.PlaytimeStatisticsService.GetIsoWeekStart(today);
        var baseDuration = TimeSpan.FromMinutes(60) + liveIncrease;
        var days = Enumerable.Range(0, 7)
            .Select(offset => new DailyPlaytimeInfo(
                weekStart.AddDays(offset),
                weekStart.AddDays(offset) == today ? baseDuration : TimeSpan.Zero))
            .ToArray();

        return new DashboardStats(
            baseDuration,
            TimeSpan.FromMinutes(30),
            baseDuration,
            TimeSpan.FromMinutes(45),
            TimeSpan.FromHours(10) + liveIncrease,
            1,
            1,
            1,
            days,
            [],
            [new RecentGameInfo(1, "Test Game", now, baseDuration, true)]);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class FakeStatisticsService(DashboardStats current) : IPlaytimeStatisticsService
    {
        public DashboardStats Current { get; set; } = current;

        public Task<DashboardStats> GetDashboardStatsAsync(TimeZoneInfo localTimeZone, CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task<PlaytimeStatistics> GetStatisticsAsync(StatisticsPeriodKind period, TimeZoneInfo localTimeZone, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<TimeSpan> GetTotalDurationAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current.Total);

        public Task<TimeSpan> GetDurationForLocalRangeAsync(DateOnly localStart, DateOnly localEndExclusive, TimeZoneInfo localTimeZone, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DailyPlaytimeInfo>> GetActivityHeatmapAsync(int weekCount, TimeZoneInfo localTimeZone, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTrackingService : IGameTrackingService
    {
        public TrackingState State { get; } = new(true, false, []);

        public event EventHandler<TrackingState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RecoverOpenSessionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ScanOnceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
