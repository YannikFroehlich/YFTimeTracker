using System.Collections.Concurrent;
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
    // Obergrenze für die errechnete Wartezeit. Sie fängt Fälle ab, in denen die Spielzeit nicht
    // durch die laufende Session wächst, etwa wenn nachträglich eine Session von Hand ergänzt wird.
    private static readonly TimeSpan MaximumCheckInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, byte> notifiedReferenceKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> nextCheckByReferenceKey = new(StringComparer.Ordinal);
    private DateOnly cachedDay;
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

    internal static string CreateReferenceKey(string scope, long gameId, DateOnly periodStart)
    {
        return $"playtime-limit:{scope}:{gameId}:{periodStart:yyyy-MM-dd}";
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

    internal async Task CheckGameAsync(RunningGameInfo runningGame)
    {
        var game = await games.GetByIdAsync(runningGame.GameId, CancellationToken.None);
        if (game is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);
        ForgetKeysOfPreviousDays(today);

        if (game.DailyPlaytimeLimitMinutes is { } dailyLimit)
        {
            await CheckLimitAsync(
                game,
                "daily",
                today,
                today.AddDays(1),
                dailyLimit,
                "Tageslimit erreicht",
                $"{game.Name}: Das heutige Zeitlimit von {dailyLimit} Minuten ist erreicht.");
        }

        if (game.WeeklyPlaytimeLimitMinutes is { } weeklyLimit)
        {
            var weekStart = PlaytimeStatisticsService.GetIsoWeekStart(today);
            await CheckLimitAsync(
                game,
                "weekly",
                weekStart,
                weekStart.AddDays(7),
                weeklyLimit,
                "Wochenlimit erreicht",
                $"{game.Name}: Das wöchentliche Zeitlimit von {weeklyLimit} Minuten ist erreicht.");
        }
    }

    private async Task CheckLimitAsync(
        Game game,
        string scope,
        DateOnly periodStart,
        DateOnly periodEndExclusive,
        int limitMinutes,
        string title,
        string message)
    {
        var referenceKey = CreateReferenceKey(scope, game.Id, periodStart);
        if (notifiedReferenceKeys.ContainsKey(referenceKey))
        {
            return;
        }

        var now = clock.UtcNow;
        if (nextCheckByReferenceKey.TryGetValue(referenceKey, out var nextCheckAtUtc) && now < nextCheckAtUtc)
        {
            return;
        }

        var duration = await statistics.GetDurationForGameAndLocalRangeAsync(
            game.Id, periodStart, periodEndExclusive, TimeZoneInfo.Local, CancellationToken.None);
        if (duration.TotalMinutes < limitMinutes)
        {
            // Die Spielzeit wächst höchstens in Echtzeit, weil pro Spiel nur eine Session offen sein
            // kann. Vor Ablauf der Restzeit ist das Limit deshalb nicht erreichbar und eine erneute
            // Abfrage überflüssig.
            var remaining = TimeSpan.FromMinutes(limitMinutes) - duration;
            var delay = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            nextCheckByReferenceKey[referenceKey] = now + (delay > MaximumCheckInterval
                ? MaximumCheckInterval
                : delay);
            return;
        }

        if (!notifiedReferenceKeys.TryAdd(referenceKey, 0))
        {
            return;
        }

        nextCheckByReferenceKey.TryRemove(referenceKey, out _);

        if (await notificationLog.ExistsAsync(
                NotificationKind.PlaytimeLimitReached,
                referenceKey,
                CancellationToken.None))
        {
            logger.LogDebug(
                "Skipped a repeated playtime limit notification for {ReferenceKey}; it was already reported.",
                referenceKey);
            return;
        }

        trayService.ShowBalloonNotification(title, message);
        await LogNotificationAsync(NotificationKind.PlaytimeLimitReached, title, message, game.Id, referenceKey);
    }

    private void ForgetKeysOfPreviousDays(DateOnly today)
    {
        if (cachedDay == today)
        {
            return;
        }

        cachedDay = today;
        notifiedReferenceKeys.Clear();
        nextCheckByReferenceKey.Clear();
    }

    private async Task LogNotificationAsync(
        NotificationKind kind,
        string title,
        string message,
        long gameId,
        string referenceKey)
    {
        try
        {
            await notificationLog.AddAsync(new NotificationLogEntry
            {
                Kind = kind,
                Title = title,
                Message = message,
                CreatedAtUtc = clock.UtcNow,
                RelatedGameId = gameId,
                ReferenceKey = referenceKey
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to write notification log entry for game {GameId}.", gameId);
        }
    }
}
