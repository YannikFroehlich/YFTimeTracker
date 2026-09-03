using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.App.Services;

public sealed class PlaytimeLimitNotifier(
    IGameTrackingService trackingService,
    IGameRepository games,
    IPlaytimeStatisticsService statistics,
    ITrayService trayService,
    INotificationLogRepository notificationLog,
    IClock clock,
    ILogger<PlaytimeLimitNotifier> logger) : IPlaytimeLimitNotifier
{
    private readonly Dictionary<long, DateOnly> lastDailyNotificationDate = new();
    private readonly Dictionary<long, DateOnly> lastWeeklyNotificationWeekStart = new();
    private bool initialized;

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        trackingService.StateChanged += TrackingService_StateChanged;
    }

    public void Dispose()
    {
        trackingService.StateChanged -= TrackingService_StateChanged;
    }

    private async void TrackingService_StateChanged(object? sender, TrackingState state)
    {
        foreach (var runningGame in state.RunningGames)
        {
            try
            {
                await CheckGameAsync(runningGame);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Playtime limit check failed for game {GameId}.", runningGame.GameId);
            }
        }
    }

    private async Task CheckGameAsync(RunningGameInfo runningGame)
    {
        var game = await games.GetByIdAsync(runningGame.GameId, CancellationToken.None);
        if (game is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);

        if (game.DailyPlaytimeLimitMinutes is { } dailyLimit
            && lastDailyNotificationDate.GetValueOrDefault(game.Id) != today)
        {
            var todayDuration = await statistics.GetDurationForGameAndLocalRangeAsync(
                game.Id, today, today.AddDays(1), TimeZoneInfo.Local, CancellationToken.None);
            if (todayDuration.TotalMinutes >= dailyLimit)
            {
                lastDailyNotificationDate[game.Id] = today;
                var message = $"{game.Name}: Das heutige Zeitlimit von {dailyLimit} Minuten ist erreicht.";
                trayService.ShowBalloonNotification("Tageslimit erreicht", message);
                await LogNotificationAsync(NotificationKind.PlaytimeLimitReached, "Tageslimit erreicht", message, game.Id);
            }
        }

        if (game.WeeklyPlaytimeLimitMinutes is { } weeklyLimit)
        {
            var weekStart = PlaytimeStatisticsService.GetIsoWeekStart(today);
            if (lastWeeklyNotificationWeekStart.GetValueOrDefault(game.Id) != weekStart)
            {
                var weekDuration = await statistics.GetDurationForGameAndLocalRangeAsync(
                    game.Id, weekStart, weekStart.AddDays(7), TimeZoneInfo.Local, CancellationToken.None);
                if (weekDuration.TotalMinutes >= weeklyLimit)
                {
                    lastWeeklyNotificationWeekStart[game.Id] = weekStart;
                    var message = $"{game.Name}: Das wöchentliche Zeitlimit von {weeklyLimit} Minuten ist erreicht.";
                    trayService.ShowBalloonNotification("Wochenlimit erreicht", message);
                    await LogNotificationAsync(NotificationKind.PlaytimeLimitReached, "Wochenlimit erreicht", message, game.Id);
                }
            }
        }
    }

    private async Task LogNotificationAsync(NotificationKind kind, string title, string message, long gameId)
    {
        try
        {
            await notificationLog.AddAsync(new NotificationLogEntry
            {
                Kind = kind,
                Title = title,
                Message = message,
                CreatedAtUtc = clock.UtcNow,
                RelatedGameId = gameId
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to write notification log entry for game {GameId}.", gameId);
        }
    }
}
