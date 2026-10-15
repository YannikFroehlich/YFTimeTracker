using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private const string MutedColor = "#9D9A91";
    private const string RecentMutedColor = "#B5B1A7";
    private const string GreenColor = "#6FD49A";
    private const string RedColor = "#FF7B70";
    private const string AccentColor = "#FF6A2B";
    private const string BarColor = "#77736A";
    private const string ProgressNormalColor = "#FF6A2B";
    private const string ProgressLimitReachedColor = "#FF5368";
    private readonly IPlaytimeStatisticsService statistics;
    private readonly IGameTrackingService trackingService;
    private readonly IGameRepository games;
    private readonly IClock clock;
    private readonly IGameIconService? gameIcons;
    private DateTimeOffset? activeStartedAtUtc;
    private bool isRefreshing;

    public DashboardViewModel(
        IPlaytimeStatisticsService statistics,
        IGameTrackingService trackingService,
        IGameRepository games,
        IClock clock,
        IGameIconService? gameIcons = null)
    {
        this.statistics = statistics;
        this.trackingService = trackingService;
        this.games = games;
        this.clock = clock;
        this.gameIcons = gameIcons;
    }

    [ObservableProperty]
    public partial string TodayText { get; private set; } = "0 min";

    [ObservableProperty]
    public partial string TodaySessionText { get; private set; } = "Keine Sessions";

    [ObservableProperty]
    public partial string TodayComparisonText { get; private set; } = "Keine Änderung";

    [ObservableProperty]
    public partial string TodayComparisonBrush { get; private set; } = MutedColor;

    [ObservableProperty]
    public partial string WeekText { get; private set; } = "0 min";

    [ObservableProperty]
    public partial string WeekSessionText { get; private set; } = "Keine Sessions";

    [ObservableProperty]
    public partial string WeekComparisonText { get; private set; } = "Keine Änderung";

    [ObservableProperty]
    public partial string WeekComparisonBrush { get; private set; } = MutedColor;

    [ObservableProperty]
    public partial string TotalText { get; private set; } = "0 min";

    [ObservableProperty]
    public partial string GamesPlayedText { get; private set; } = "Noch keine Spiele";

    [ObservableProperty]
    public partial string PauseButtonText { get; private set; } = "Tracking pausieren";

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Tracking wird vorbereitet";

    [ObservableProperty]
    public partial string StatusDetailText { get; private set; } = "Spielzeit wird automatisch erfasst";

    [ObservableProperty]
    public partial string ActiveGameName { get; private set; } = "Kein aktives Spiel";

    [ObservableProperty]
    public partial string ActiveGameInitials { get; private set; } = "YF";

    [ObservableProperty]
    public partial string? ActiveGameIconPath { get; private set; }

    [ObservableProperty]
    public partial string ActiveElapsedText { get; private set; } = "00:00:00";

    [ObservableProperty]
    public partial string ActiveGameHint { get; private set; } = "Starte ein registriertes Spiel, um die Live-Ansicht zu aktivieren.";

    [ObservableProperty]
    public partial string AdditionalRunningGamesText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility ActiveGameVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility ActiveEmptyVisibility { get; private set; } = Visibility.Visible;

    [ObservableProperty]
    public partial Visibility RecentGamesVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility RecentEmptyVisibility { get; private set; } = Visibility.Visible;

    [ObservableProperty]
    public partial string ChartMaximumText { get; private set; } = "2 h";

    [ObservableProperty]
    public partial string ChartMiddleText { get; private set; } = "1 h";

    [ObservableProperty]
    public partial string ActiveDailyProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double ActiveDailyProgressPercent { get; private set; }

    [ObservableProperty]
    public partial string ActiveDailyProgressColor { get; private set; } = ProgressNormalColor;

    [ObservableProperty]
    public partial Visibility ActiveDailyProgressVisibility { get; private set; } = Visibility.Collapsed;

    public ObservableCollection<WeeklyBarViewModel> WeekDays { get; } = [];

    public ObservableCollection<RecentGameItemViewModel> RecentGames { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            var stats = await statistics.GetDashboardStatsAsync(TimeZoneInfo.Local, CancellationToken.None);

            TodayText = TimeFormatter.Format(stats.Today);
            TodaySessionText = FormatSessionCount(stats.TodaySessionCount);
            (TodayComparisonText, TodayComparisonBrush) = FormatComparison(stats.Today, stats.PreviousDay);

            WeekText = TimeFormatter.Format(stats.CurrentWeek);
            WeekSessionText = FormatSessionCount(stats.CurrentWeekSessionCount);
            (WeekComparisonText, WeekComparisonBrush) = FormatComparison(stats.CurrentWeek, stats.PreviousWeek);

            TotalText = TimeFormatter.Format(stats.Total);
            GamesPlayedText = stats.GamesPlayedCount == 0
                ? "Noch keine Spiele"
                : $"{stats.GamesPlayedCount} {Pluralize(stats.GamesPlayedCount, "Spiel", "Spiele")} gespielt";

            PauseButtonText = trackingService.State.IsPaused ? "Tracking fortsetzen" : "Tracking pausieren";
            StatusText = trackingService.State.IsPaused ? "Tracking pausiert" : "Tracking aktiv";
            StatusDetailText = trackingService.State.IsPaused
                ? "Während der Pause wird keine Spielzeit erfasst"
                : "Spielzeit wird automatisch erfasst";

            var iconPaths = await ResolveIconPathsAsync(stats.RunningGames, stats.RecentGames);
            await UpdateActiveGameAsync(stats.RunningGames, iconPaths);
            UpdateWeekChart(stats.CurrentWeekDays);
            UpdateRecentGames(stats.RecentGames, iconPaths);
        }
        finally
        {
            isRefreshing = false;
        }
    }

    public void Tick()
    {
        if (activeStartedAtUtc is null)
        {
            return;
        }

        ActiveElapsedText = TimeFormatter.FormatClock(clock.UtcNow - activeStartedAtUtc.Value);
    }

    [RelayCommand]
    private async Task ToggleTrackingAsync()
    {
        if (trackingService.State.IsPaused)
        {
            await trackingService.ResumeAsync(CancellationToken.None);
        }
        else
        {
            await trackingService.PauseAsync(CancellationToken.None);
        }

        await RefreshAsync();
    }

    private async Task UpdateActiveGameAsync(
        IReadOnlyList<RunningGameInfo> runningGames,
        IReadOnlyDictionary<long, string?> iconPaths)
    {
        var activeGame = runningGames.FirstOrDefault();
        if (activeGame is null)
        {
            activeStartedAtUtc = null;
            ActiveGameName = "Kein aktives Spiel";
            ActiveGameInitials = "YF";
            ActiveGameIconPath = null;
            ActiveElapsedText = "00:00:00";
            ActiveGameHint = trackingService.State.IsPaused
                ? "Setze das Tracking fort, damit laufende Spiele erkannt werden."
                : "Starte ein registriertes Spiel, um die Live-Ansicht zu aktivieren.";
            AdditionalRunningGamesText = string.Empty;
            ActiveGameVisibility = Visibility.Collapsed;
            ActiveEmptyVisibility = Visibility.Visible;
            ActiveDailyProgressVisibility = Visibility.Collapsed;
            return;
        }

        activeStartedAtUtc = activeGame.StartedAtUtc;
        ActiveGameName = activeGame.Name;
        ActiveGameInitials = GetInitials(activeGame.Name);
        ActiveGameIconPath = iconPaths.GetValueOrDefault(activeGame.GameId);
        ActiveElapsedText = TimeFormatter.FormatClock(activeGame.Duration);
        ActiveGameHint = "Automatisch über den laufenden Prozess erkannt";
        AdditionalRunningGamesText = runningGames.Count > 1
            ? $"+ {runningGames.Count - 1} weitere aktive {Pluralize(runningGames.Count - 1, "Session", "Sessions")}"
            : string.Empty;
        ActiveGameVisibility = Visibility.Visible;
        ActiveEmptyVisibility = Visibility.Collapsed;

        await UpdateActiveDailyProgressAsync(activeGame.GameId);
    }

    private async Task UpdateActiveDailyProgressAsync(long activeGameId)
    {
        var activeGameEntity = await games.GetByIdAsync(activeGameId, CancellationToken.None);
        if (activeGameEntity?.DailyPlaytimeLimitMinutes is not { } limit || limit <= 0)
        {
            ActiveDailyProgressVisibility = Visibility.Collapsed;
            return;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);
        var minutes = (await statistics.GetDurationForGameAndLocalRangeAsync(
            activeGameId,
            today,
            today.AddDays(1),
            TimeZoneInfo.Local,
            CancellationToken.None)).TotalMinutes;

        ActiveDailyProgressText = $"{minutes:0} / {limit} Min heute";
        ActiveDailyProgressPercent = Math.Clamp(minutes / limit * 100, 0, 100);
        ActiveDailyProgressColor = minutes >= limit ? ProgressLimitReachedColor : ProgressNormalColor;
        ActiveDailyProgressVisibility = Visibility.Visible;
    }

    private void UpdateWeekChart(IReadOnlyList<DailyPlaytimeInfo> days)
    {
        var maximumHours = Math.Max(2, Math.Ceiling(days.Count == 0 ? 0 : days.Max(day => day.Duration.TotalHours)));
        ChartMaximumText = $"{maximumHours:0} h";
        ChartMiddleText = $"{maximumHours / 2:0.#} h";
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);

        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            var height = day.Duration <= TimeSpan.Zero
                ? 4
                : Math.Max(12, day.Duration.TotalHours / maximumHours * 132);
            var dayLabel = day.Date.ToDateTime(TimeOnly.MinValue).ToString("ddd", GermanCulture).TrimEnd('.');
            var durationText = TimeFormatter.Format(day.Duration);
            var brush = day.Date == today ? AccentColor : BarColor;
            if (index < WeekDays.Count)
            {
                WeekDays[index].Update(dayLabel, durationText, height, maximumHours, brush);
            }
            else
            {
                WeekDays.Add(new WeeklyBarViewModel(dayLabel, durationText, height, maximumHours, brush));
            }
        }

        while (WeekDays.Count > days.Count)
        {
            WeekDays.RemoveAt(WeekDays.Count - 1);
        }
    }

    private void UpdateRecentGames(
        IReadOnlyList<RecentGameInfo> games,
        IReadOnlyDictionary<long, string?> iconPaths)
    {
        var visibleGames = games.Take(6).ToArray();
        for (var index = 0; index < visibleGames.Length; index++)
        {
            var game = visibleGames[index];
            var existingIndex = FindRecentGameIndex(game.GameId, index);
            if (existingIndex < 0)
            {
                RecentGames.Insert(index, new RecentGameItemViewModel(game.GameId));
            }
            else if (existingIndex != index)
            {
                RecentGames.Move(existingIndex, index);
            }

            RecentGames[index].Update(
                game.Name,
                GetInitials(game.Name),
                iconPaths.GetValueOrDefault(game.GameId),
                TimeFormatter.Format(game.TotalDuration),
                game.IsRunning ? "Jetzt aktiv" : game.LastPlayedAtUtc.LocalDateTime.ToString("dd.MM.yyyy · HH:mm", GermanCulture),
                game.IsRunning ? GreenColor : RecentMutedColor);
        }

        while (RecentGames.Count > visibleGames.Length)
        {
            RecentGames.RemoveAt(RecentGames.Count - 1);
        }

        RecentGamesVisibility = RecentGames.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentEmptyVisibility = RecentGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<IReadOnlyDictionary<long, string?>> ResolveIconPathsAsync(
        IReadOnlyList<RunningGameInfo> runningGames,
        IReadOnlyList<RecentGameInfo> recentGames)
    {
        if (gameIcons is null)
        {
            return new Dictionary<long, string?>();
        }

        var games = runningGames
            .Select(game => (game.GameId, game.ExecutablePath))
            .Concat(recentGames.Select(game => (game.GameId, game.ExecutablePath)))
            .GroupBy(game => game.GameId)
            .Select(group => group
                .OrderByDescending(game => !string.IsNullOrWhiteSpace(game.ExecutablePath))
                .First())
            .ToArray();
        var tasks = games.Select(async game => new
        {
            game.GameId,
            IconPath = await gameIcons.GetGameImagePathAsync(game.GameId, game.ExecutablePath, CancellationToken.None)
        });
        var resolved = await Task.WhenAll(tasks);
        return resolved.ToDictionary(item => item.GameId, item => item.IconPath);
    }

    private int FindRecentGameIndex(long gameId, int startIndex)
    {
        for (var index = startIndex; index < RecentGames.Count; index++)
        {
            if (RecentGames[index].GameId == gameId)
            {
                return index;
            }
        }

        return -1;
    }

    private static (string Text, string Brush) FormatComparison(TimeSpan current, TimeSpan previous)
    {
        if (previous <= TimeSpan.Zero)
        {
            return current <= TimeSpan.Zero
                ? ("Keine Änderung", MutedColor)
                : ("Neu in diesem Zeitraum", GreenColor);
        }

        var change = (current.TotalSeconds - previous.TotalSeconds) / previous.TotalSeconds * 100;
        var prefix = change > 0 ? "+" : string.Empty;
        var brush = change > 0
            ? GreenColor
            : change < 0
                ? RedColor
                : MutedColor;
        return ($"{prefix}{change:0}%", brush);
    }

    private static string FormatSessionCount(int count)
    {
        return count == 0 ? "Keine Sessions" : $"{count} {Pluralize(count, "Session", "Sessions")}";
    }

    private static string Pluralize(int count, string singular, string plural) => count == 1 ? singular : plural;

    private static string GetInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "?";
        }

        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

}

public sealed class WeeklyBarViewModel : ObservableObject
{
    private string dayLabel;
    private string durationText;
    private double barHeight;
    private double scaleMaximum;
    private string barBrush;

    public WeeklyBarViewModel(
        string dayLabel,
        string durationText,
        double barHeight,
        double scaleMaximum,
        string barBrush)
    {
        this.dayLabel = dayLabel;
        this.durationText = durationText;
        this.barHeight = barHeight;
        this.scaleMaximum = scaleMaximum;
        this.barBrush = barBrush;
    }

    public string DayLabel { get => dayLabel; private set => SetProperty(ref dayLabel, value); }

    public string DurationText { get => durationText; private set => SetProperty(ref durationText, value); }

    public double BarHeight { get => barHeight; private set => SetProperty(ref barHeight, value); }

    public string BarBrush { get => barBrush; private set => SetProperty(ref barBrush, value); }

    public void Update(string newDayLabel, string newDurationText, double newBarHeight, double newScaleMaximum, string newBarBrush)
    {
        DayLabel = newDayLabel;
        var durationChanged = !string.Equals(DurationText, newDurationText, StringComparison.Ordinal);
        DurationText = newDurationText;
        if (durationChanged || !scaleMaximum.Equals(newScaleMaximum))
        {
            scaleMaximum = newScaleMaximum;
            BarHeight = newBarHeight;
        }

        BarBrush = newBarBrush;
    }
}

public sealed partial class RecentGameItemViewModel(long gameId) : ObservableObject
{
    private string name = string.Empty;

    public long GameId { get; } = gameId;

    public string Name { get => name; private set => SetProperty(ref name, value); }

    [ObservableProperty]
    public partial string Initials { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? IconPath { get; private set; }

    [ObservableProperty]
    public partial string TotalPlaytime { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LastSession { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LastSessionBrush { get; private set; } = "#B5B1A7";

    public void Update(
        string newName,
        string newInitials,
        string? newIconPath,
        string newTotalPlaytime,
        string newLastSession,
        string newLastSessionBrush)
    {
        Name = newName;
        Initials = newInitials;
        IconPath = newIconPath;
        TotalPlaytime = newTotalPlaytime;
        LastSession = newLastSession;
        LastSessionBrush = newLastSessionBrush;
    }
}
