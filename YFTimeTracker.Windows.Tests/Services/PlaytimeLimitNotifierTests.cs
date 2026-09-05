using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.Services;

[TestClass]
public sealed class PlaytimeLimitNotifierTests
{
    private static readonly DateTimeOffset NowUtc = DateTimeOffset.Parse("2026-09-05T12:00:00Z");

    [TestMethod]
    public async Task Reached_daily_limit_is_reported_once_and_stored_with_a_reference_key()
    {
        var game = CreateGame(dailyLimitMinutes: 60);
        var tray = new FakeTrayService();
        var log = new FakeNotificationLog();
        var notifier = CreateNotifier(game, TimeSpan.FromMinutes(75), tray, log);

        await notifier.CheckGameAsync(CreateRunningGame(game));
        await notifier.CheckGameAsync(CreateRunningGame(game));

        Assert.HasCount(1, tray.Notifications);
        Assert.AreEqual("Tageslimit erreicht", tray.Notifications[0].Title);
        Assert.HasCount(1, log.Entries);
        Assert.AreEqual(ExpectedDailyReferenceKey(game.Id), log.Entries[0].ReferenceKey);
        Assert.AreEqual(game.Id, log.Entries[0].RelatedGameId);
    }

    [TestMethod]
    public async Task Reached_daily_limit_is_not_reported_again_after_an_application_restart()
    {
        var game = CreateGame(dailyLimitMinutes: 60);
        var log = new FakeNotificationLog();

        var firstRun = CreateNotifier(game, TimeSpan.FromMinutes(75), new FakeTrayService(), log);
        await firstRun.CheckGameAsync(CreateRunningGame(game));
        Assert.HasCount(1, log.Entries);

        // Ein Neustart verwirft den Prozessspeicher, der Benachrichtigungsverlauf bleibt bestehen.
        var trayAfterRestart = new FakeTrayService();
        var secondRun = CreateNotifier(game, TimeSpan.FromMinutes(90), trayAfterRestart, log);
        await secondRun.CheckGameAsync(CreateRunningGame(game));

        Assert.IsEmpty(trayAfterRestart.Notifications);
        Assert.HasCount(1, log.Entries);
    }

    [TestMethod]
    public async Task Playtime_below_the_configured_limit_is_not_reported()
    {
        var game = CreateGame(dailyLimitMinutes: 60);
        var tray = new FakeTrayService();
        var log = new FakeNotificationLog();
        var notifier = CreateNotifier(game, TimeSpan.FromMinutes(59), tray, log);

        await notifier.CheckGameAsync(CreateRunningGame(game));

        Assert.IsEmpty(tray.Notifications);
        Assert.IsEmpty(log.Entries);
    }

    [TestMethod]
    public async Task Daily_and_weekly_limits_are_reported_separately()
    {
        var game = CreateGame(dailyLimitMinutes: 60, weeklyLimitMinutes: 70);
        var tray = new FakeTrayService();
        var log = new FakeNotificationLog();
        var notifier = CreateNotifier(game, TimeSpan.FromMinutes(75), tray, log);

        await notifier.CheckGameAsync(CreateRunningGame(game));

        Assert.HasCount(2, tray.Notifications);
        Assert.AreEqual("Tageslimit erreicht", tray.Notifications[0].Title);
        Assert.AreEqual("Wochenlimit erreicht", tray.Notifications[1].Title);
        Assert.HasCount(2, log.Entries);
        Assert.AreEqual(2, log.Entries.Select(entry => entry.ReferenceKey).Distinct().Count());
    }

    private static string ExpectedDailyReferenceKey(long gameId)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(NowUtc, TimeZoneInfo.Local).Date);
        return PlaytimeLimitNotifier.CreateReferenceKey("daily", gameId, today);
    }

    private static PlaytimeLimitNotifier CreateNotifier(
        Game game,
        TimeSpan playedDuration,
        FakeTrayService tray,
        FakeNotificationLog log)
    {
        return new PlaytimeLimitNotifier(
            new FakeTrackingService(),
            new FakeGameRepository(game),
            new FakeStatisticsService(playedDuration),
            tray,
            log,
            new FakeClock(NowUtc),
            NullLogger<PlaytimeLimitNotifier>.Instance);
    }

    private static Game CreateGame(int? dailyLimitMinutes = null, int? weeklyLimitMinutes = null) => new()
    {
        Id = 7,
        Name = "Neon Game",
        AddedAtUtc = NowUtc,
        DailyPlaytimeLimitMinutes = dailyLimitMinutes,
        WeeklyPlaytimeLimitMinutes = weeklyLimitMinutes
    };

    private static RunningGameInfo CreateRunningGame(Game game) => new(
        game.Id,
        game.Name,
        NowUtc.AddHours(-2),
        TimeSpan.FromHours(2));

    private sealed record TrayNotification(string Title, string Message);

    private sealed class FakeTrayService : ITrayService
    {
        public List<TrayNotification> Notifications { get; } = [];

        public void Initialize(App.MainWindow mainWindow)
        {
        }

        public void ShowBalloonNotification(string title, string message)
        {
            Notifications.Add(new TrayNotification(title, message));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeNotificationLog : INotificationLogRepository
    {
        public List<NotificationLogEntry> Entries { get; } = [];

        public event EventHandler? EntryAdded;

        public Task<NotificationLogEntry> AddAsync(NotificationLogEntry entry, CancellationToken cancellationToken)
        {
            entry.Id = Entries.Count + 1;
            Entries.Add(entry);
            EntryAdded?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(entry);
        }

        public Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<NotificationLogEntry>>(Entries.TakeLast(count).ToArray());
        }

        public Task<bool> ExistsAsync(NotificationKind kind, string referenceKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(Entries.Any(entry => entry.Kind == kind && entry.ReferenceKey == referenceKey));
        }

        public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Entries.Count(entry => !entry.IsRead));
        }

        public Task MarkAllAsReadAsync(CancellationToken cancellationToken)
        {
            Entries.ForEach(entry => entry.IsRead = true);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(long id, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }

        public Task ClearAllAsync(CancellationToken cancellationToken)
        {
            Entries.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStatisticsService(TimeSpan duration) : IPlaytimeStatisticsService
    {
        public Task<DashboardStats> GetDashboardStatsAsync(TimeZoneInfo localTimeZone, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<PlaytimeStatistics> GetStatisticsAsync(StatisticsPeriodKind period, TimeZoneInfo localTimeZone, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TimeSpan> GetTotalDurationAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TimeSpan> GetDurationForLocalRangeAsync(DateOnly localStart, DateOnly localEndExclusive, TimeZoneInfo localTimeZone, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TimeSpan> GetDurationForGameAndLocalRangeAsync(long gameId, DateOnly localStart, DateOnly localEndExclusive, TimeZoneInfo localTimeZone, CancellationToken cancellationToken)
            => Task.FromResult(duration);

        public Task<IReadOnlyList<DailyPlaytimeInfo>> GetActivityHeatmapAsync(int weekCount, TimeZoneInfo localTimeZone, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeGameRepository(Game game) : IGameRepository
    {
        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Game>>([game]);

        public Task<Game?> GetByIdAsync(long id, CancellationToken cancellationToken)
            => Task.FromResult(id == game.Id ? game : null);

        public Task<Game?> GetByExecutablePathKeyAsync(string executablePathKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Game?> GetByExternalIdAsync(GameSource source, string externalGameId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Game> AddAsync(Game newGame, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdateAsync(Game updatedGame, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<GameExecutable> AddExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SetPrimaryExecutableAsync(long gameId, GameExecutable executable, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteAsync(long id, CancellationToken cancellationToken)
            => throw new NotSupportedException();
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

    private sealed class FakeClock(DateTimeOffset nowUtc) : IClock
    {
        public DateTimeOffset UtcNow { get; } = nowUtc;
    }
}
