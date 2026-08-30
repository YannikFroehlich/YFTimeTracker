using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private const string MutedColor = "#8391A8";
    private const string RecentMutedColor = "#A7B2C7";
    private const string GreenColor = "#29E7A4";
    private const string RedColor = "#FF6B7A";
    private const string CyanColor = "#2CE5F3";
    private const string BlueColor = "#387BFF";
    private readonly IPlaytimeStatisticsService statistics;
    private readonly IGameTrackingService trackingService;
    private readonly IClock clock;
    private DateTimeOffset? activeStartedAtUtc;
    private bool isRefreshing;
    private string todayText = "0 min";
    private string todaySessionText = "Keine Sessions";
    private string todayComparisonText = "Keine Änderung";
    private string todayComparisonBrush = MutedColor;
    private string weekText = "0 min";
    private string weekSessionText = "Keine Sessions";
    private string weekComparisonText = "Keine Änderung";
    private string weekComparisonBrush = MutedColor;
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

    public string TodayComparisonBrush { get => todayComparisonBrush; private set => SetProperty(ref todayComparisonBrush, value); }

    public string WeekText { get => weekText; private set => SetProperty(ref weekText, value); }

    public string WeekSessionText { get => weekSessionText; private set => SetProperty(ref weekSessionText, value); }

    public string WeekComparisonText { get => weekComparisonText; private set => SetProperty(ref weekComparisonText, value); }

    public string WeekComparisonBrush { get => weekComparisonBrush; private set => SetProperty(ref weekComparisonBrush, value); }

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
            var brush = day.Date == today ? CyanColor : BlueColor;
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

    private void UpdateRecentGames(IReadOnlyList<RecentGameInfo> games)
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

public sealed class RecentGameItemViewModel(long gameId) : ObservableObject
{
    private string name = string.Empty;
    private string initials = string.Empty;
    private string totalPlaytime = string.Empty;
    private string lastSession = string.Empty;
    private string lastSessionBrush = "#A7B2C7";

    public long GameId { get; } = gameId;

    public string Name { get => name; private set => SetProperty(ref name, value); }

    public string Initials { get => initials; private set => SetProperty(ref initials, value); }

    public string TotalPlaytime { get => totalPlaytime; private set => SetProperty(ref totalPlaytime, value); }

    public string LastSession { get => lastSession; private set => SetProperty(ref lastSession, value); }

    public string LastSessionBrush { get => lastSessionBrush; private set => SetProperty(ref lastSessionBrush, value); }

    public void Update(
        string newName,
        string newInitials,
        string newTotalPlaytime,
        string newLastSession,
        string newLastSessionBrush)
    {
        Name = newName;
        Initials = newInitials;
        TotalPlaytime = newTotalPlaytime;
        LastSession = newLastSession;
        LastSessionBrush = newLastSessionBrush;
    }
}
