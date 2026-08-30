using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly IPlaytimeStatisticsService statistics;
    private readonly IGameTrackingService trackingService;
    private readonly IClock clock;
    private DateTimeOffset? activeStartedAtUtc;
    private bool isRefreshing;
    private string todayText = "0 min";
    private string todaySessionText = "Keine Sessions";
    private string todayComparisonText = "Keine Änderung";
    private Brush todayComparisonBrush = CreateBrush(0x83, 0x91, 0xA8);
    private string weekText = "0 min";
    private string weekSessionText = "Keine Sessions";
    private string weekComparisonText = "Keine Änderung";
    private Brush weekComparisonBrush = CreateBrush(0x83, 0x91, 0xA8);
    private string totalText = "0 min";
    private string gamesPlayedText = "Noch keine Spiele";
    private string pauseButtonText = "Tracking pausieren";
    private string statusText = "Tracking wird vorbereitet";
    private string statusDetailText = "Spielzeit wird automatisch erfasst";
    private string activeGameName = "Kein aktives Spiel";
    private string activeGameInitials = "YF";
    private string activeElapsedText = "00:00:00";
    private string activeGameHint = "Starte ein registriertes Spiel, um die Live-Ansicht zu aktivieren.";
    private string additionalRunningGamesText = string.Empty;
    private Visibility activeGameVisibility = Visibility.Collapsed;
    private Visibility activeEmptyVisibility = Visibility.Visible;
    private Visibility recentGamesVisibility = Visibility.Collapsed;
    private Visibility recentEmptyVisibility = Visibility.Visible;
    private string chartMaximumText = "2 h";
    private string chartMiddleText = "1 h";

    public DashboardViewModel(
        IPlaytimeStatisticsService statistics,
        IGameTrackingService trackingService,
        IClock clock)
    {
        this.statistics = statistics;
        this.trackingService = trackingService;
        this.clock = clock;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ToggleTrackingCommand = new AsyncRelayCommand(ToggleTrackingAsync);
    }

    public string TodayText { get => todayText; private set => SetProperty(ref todayText, value); }

    public string TodaySessionText { get => todaySessionText; private set => SetProperty(ref todaySessionText, value); }

    public string TodayComparisonText { get => todayComparisonText; private set => SetProperty(ref todayComparisonText, value); }

    public Brush TodayComparisonBrush { get => todayComparisonBrush; private set => SetProperty(ref todayComparisonBrush, value); }

    public string WeekText { get => weekText; private set => SetProperty(ref weekText, value); }

    public string WeekSessionText { get => weekSessionText; private set => SetProperty(ref weekSessionText, value); }

    public string WeekComparisonText { get => weekComparisonText; private set => SetProperty(ref weekComparisonText, value); }

    public Brush WeekComparisonBrush { get => weekComparisonBrush; private set => SetProperty(ref weekComparisonBrush, value); }

    public string TotalText { get => totalText; private set => SetProperty(ref totalText, value); }

    public string GamesPlayedText { get => gamesPlayedText; private set => SetProperty(ref gamesPlayedText, value); }

    public string PauseButtonText { get => pauseButtonText; private set => SetProperty(ref pauseButtonText, value); }

    public string StatusText { get => statusText; private set => SetProperty(ref statusText, value); }

    public string StatusDetailText { get => statusDetailText; private set => SetProperty(ref statusDetailText, value); }

    public string ActiveGameName { get => activeGameName; private set => SetProperty(ref activeGameName, value); }

    public string ActiveGameInitials { get => activeGameInitials; private set => SetProperty(ref activeGameInitials, value); }

    public string ActiveElapsedText { get => activeElapsedText; private set => SetProperty(ref activeElapsedText, value); }

    public string ActiveGameHint { get => activeGameHint; private set => SetProperty(ref activeGameHint, value); }

    public string AdditionalRunningGamesText { get => additionalRunningGamesText; private set => SetProperty(ref additionalRunningGamesText, value); }

    public Visibility ActiveGameVisibility { get => activeGameVisibility; private set => SetProperty(ref activeGameVisibility, value); }

    public Visibility ActiveEmptyVisibility { get => activeEmptyVisibility; private set => SetProperty(ref activeEmptyVisibility, value); }

    public Visibility RecentGamesVisibility { get => recentGamesVisibility; private set => SetProperty(ref recentGamesVisibility, value); }

    public Visibility RecentEmptyVisibility { get => recentEmptyVisibility; private set => SetProperty(ref recentEmptyVisibility, value); }

    public string ChartMaximumText { get => chartMaximumText; private set => SetProperty(ref chartMaximumText, value); }

    public string ChartMiddleText { get => chartMiddleText; private set => SetProperty(ref chartMiddleText, value); }

    public ObservableCollection<WeeklyBarViewModel> WeekDays { get; } = [];

    public ObservableCollection<RecentGameItemViewModel> RecentGames { get; } = [];

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ToggleTrackingCommand { get; }

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

            UpdateActiveGame(stats.RunningGames);
            UpdateWeekChart(stats.CurrentWeekDays);
            UpdateRecentGames(stats.RecentGames);
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

    private void UpdateActiveGame(IReadOnlyList<RunningGameInfo> runningGames)
    {
        var activeGame = runningGames.FirstOrDefault();
        if (activeGame is null)
        {
            activeStartedAtUtc = null;
            ActiveGameName = "Kein aktives Spiel";
            ActiveGameInitials = "YF";
            ActiveElapsedText = "00:00:00";
            ActiveGameHint = trackingService.State.IsPaused
                ? "Setze das Tracking fort, damit laufende Spiele erkannt werden."
                : "Starte ein registriertes Spiel, um die Live-Ansicht zu aktivieren.";
            AdditionalRunningGamesText = string.Empty;
            ActiveGameVisibility = Visibility.Collapsed;
            ActiveEmptyVisibility = Visibility.Visible;
            return;
        }

        activeStartedAtUtc = activeGame.StartedAtUtc;
        ActiveGameName = activeGame.Name;
        ActiveGameInitials = GetInitials(activeGame.Name);
        ActiveElapsedText = TimeFormatter.FormatClock(activeGame.Duration);
        ActiveGameHint = "Automatisch über den laufenden Prozess erkannt";
        AdditionalRunningGamesText = runningGames.Count > 1
            ? $"+ {runningGames.Count - 1} weitere aktive {Pluralize(runningGames.Count - 1, "Session", "Sessions")}"
            : string.Empty;
        ActiveGameVisibility = Visibility.Visible;
        ActiveEmptyVisibility = Visibility.Collapsed;
    }

    private void UpdateWeekChart(IReadOnlyList<DailyPlaytimeInfo> days)
    {
        WeekDays.Clear();
        var maximumHours = Math.Max(2, Math.Ceiling(days.Count == 0 ? 0 : days.Max(day => day.Duration.TotalHours)));
        ChartMaximumText = $"{maximumHours:0} h";
        ChartMiddleText = $"{maximumHours / 2:0.#} h";
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);

        foreach (var day in days)
        {
            var height = day.Duration <= TimeSpan.Zero
                ? 4
                : Math.Max(12, day.Duration.TotalHours / maximumHours * 132);
            var dayLabel = day.Date.ToDateTime(TimeOnly.MinValue).ToString("ddd", GermanCulture).TrimEnd('.');
            WeekDays.Add(new WeeklyBarViewModel(
                dayLabel,
                TimeFormatter.Format(day.Duration),
                height,
                day.Date == today ? CreateBrush(0x2C, 0xE5, 0xF3) : CreateBrush(0x38, 0x7B, 0xFF)));
        }
    }

    private void UpdateRecentGames(IReadOnlyList<RecentGameInfo> games)
    {
        RecentGames.Clear();
        foreach (var game in games.Take(6))
        {
            RecentGames.Add(new RecentGameItemViewModel(
                game.GameId,
                game.Name,
                GetInitials(game.Name),
                TimeFormatter.Format(game.TotalDuration),
                game.IsRunning ? "Jetzt aktiv" : game.LastPlayedAtUtc.LocalDateTime.ToString("dd.MM.yyyy · HH:mm", GermanCulture),
                game.IsRunning ? CreateBrush(0x29, 0xE7, 0xA4) : CreateBrush(0xA7, 0xB2, 0xC7)));
        }

        RecentGamesVisibility = RecentGames.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        RecentEmptyVisibility = RecentGames.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static (string Text, Brush Brush) FormatComparison(TimeSpan current, TimeSpan previous)
    {
        if (previous <= TimeSpan.Zero)
        {
            return current <= TimeSpan.Zero
                ? ("Keine Änderung", CreateBrush(0x83, 0x91, 0xA8))
                : ("Neu in diesem Zeitraum", CreateBrush(0x29, 0xE7, 0xA4));
        }

        var change = (current.TotalSeconds - previous.TotalSeconds) / previous.TotalSeconds * 100;
        var prefix = change > 0 ? "+" : string.Empty;
        var brush = change > 0
            ? CreateBrush(0x29, 0xE7, 0xA4)
            : change < 0
                ? CreateBrush(0xFF, 0x6B, 0x7A)
                : CreateBrush(0x83, 0x91, 0xA8);
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

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        return new SolidColorBrush(ColorHelper.FromArgb(255, red, green, blue));
    }
}

public sealed record WeeklyBarViewModel(
    string DayLabel,
    string DurationText,
    double BarHeight,
    Brush BarBrush);

public sealed record RecentGameItemViewModel(
    long GameId,
    string Name,
    string Initials,
    string TotalPlaytime,
    string LastSession,
    Brush LastSessionBrush);
