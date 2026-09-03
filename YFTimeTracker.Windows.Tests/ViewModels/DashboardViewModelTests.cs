using System.Collections.Specialized;
using Microsoft.UI.Xaml;
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
            new FakeGameRepository(new Dictionary<long, Game>()),
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

    [TestMethod]
    public async Task Active_game_with_daily_limit_shows_progress()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var runningGame = new RunningGameInfo(1, "Test Game", now.AddMinutes(-30), TimeSpan.FromMinutes(30), @"C:\Games\Test.exe");
        var stats = CreateStats(now, TimeSpan.Zero) with { RunningGames = [runningGame] };
        var statistics = new FakeStatisticsService(stats) { GameDuration = TimeSpan.FromMinutes(45) };
        var game = new Game { Id = 1, Name = "Test Game", DailyPlaytimeLimitMinutes = 60, AddedAtUtc = now };
        var viewModel = new DashboardViewModel(
            statistics,
            new FakeTrackingService(),
            new FakeGameRepository(new Dictionary<long, Game> { [1] = game }),
            new FixedClock(now));

        await viewModel.RefreshAsync();

        Assert.AreEqual(Visibility.Visible, viewModel.ActiveDailyProgressVisibility);
        Assert.AreEqual("45 / 60 Min heute", viewModel.ActiveDailyProgressText);
        Assert.AreEqual(75, viewModel.ActiveDailyProgressPercent);
    }

    [TestMethod]
    public async Task Active_game_without_daily_limit_hides_progress()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var runningGame = new RunningGameInfo(1, "Test Game", now.AddMinutes(-30), TimeSpan.FromMinutes(30), @"C:\Games\Test.exe");
        var stats = CreateStats(now, TimeSpan.Zero) with { RunningGames = [runningGame] };
        var statistics = new FakeStatisticsService(stats);
        var game = new Game { Id = 1, Name = "Test Game", AddedAtUtc = now };
        var viewModel = new DashboardViewModel(
            statistics,
            new FakeTrackingService(),
            new FakeGameRepository(new Dictionary<long, Game> { [1] = game }),
            new FixedClock(now));

        await viewModel.RefreshAsync();

        Assert.AreEqual(Visibility.Collapsed, viewModel.ActiveDailyProgressVisibility);
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

        public TimeSpan GameDuration { get; set; }

        public Task<TimeSpan> GetDurationForGameAndLocalRangeAsync(long gameId, DateOnly localStart, DateOnly localEndExclusive, TimeZoneInfo localTimeZone, CancellationToken cancellationToken) =>
            Task.FromResult(GameDuration);

        public Task<IReadOnlyList<DailyPlaytimeInfo>> GetActivityHeatmapAsync(int weekCount, TimeZoneInfo localTimeZone, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeGameRepository(IReadOnlyDictionary<long, Game> gamesById) : IGameRepository
    {
        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Game>>(gamesById.Values.ToArray());

        public Task<Game?> GetByIdAsync(long id, CancellationToken cancellationToken) =>
            Task.FromResult(gamesById.GetValueOrDefault(id));

        public Task<Game?> GetByExecutablePathKeyAsync(string executablePathKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Game?> GetByExternalIdAsync(GameSource source, string externalGameId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Game> AddAsync(Game game, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(Game game, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GameExecutable> AddExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetPrimaryExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(long id, CancellationToken cancellationToken) => throw new NotSupportedException();
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
