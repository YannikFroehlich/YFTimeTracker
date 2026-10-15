using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed partial class YearReviewViewModel : ObservableObject
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private const string MutedColor = "#9D9A91";
    private const string GreenColor = "#6FD49A";
    private const string RedColor = "#FF7B70";
    private readonly IYearReviewService reviews;
    private readonly IClock clock;
    private readonly IExplorerService explorerService;
    private readonly IGameIconService? gameIcons;
    private YearReviewYearOption? selectedYear;
    private string? lastExportedFilePath;
    private bool initialized;
    private int refreshVersion;
    private string statusMessage = "Jahresrückblick wird vorbereitet …";

    public YearReviewViewModel(
        IYearReviewService reviews,
        IClock clock,
        IExplorerService explorerService,
        IGameIconService? gameIcons = null)
    {
        this.reviews = reviews;
        this.clock = clock;
        this.explorerService = explorerService;
        this.gameIcons = gameIcons;
    }

    public ObservableCollection<YearReviewYearOption> Years { get; } = [];

    public ObservableCollection<YearReviewMonthItemViewModel> Months { get; } = [];

    public ObservableCollection<YearReviewGameItemViewModel> TopGames { get; } = [];

    public YearReviewYearOption? SelectedYear
    {
        get => selectedYear;
        set
        {
            if (SetProperty(ref selectedYear, value) && initialized && value is not null)
            {
                _ = RefreshAsync();
            }
        }
    }

    [ObservableProperty]
    public partial string YearTitle { get; private set; } = "DEIN JAHR";

    [ObservableProperty]
    public partial string YearSubtitle { get; private set; } = "Januar bis Dezember";

    [ObservableProperty]
    public partial string TotalPlaytimeText { get; private set; } = "0 min";

    [ObservableProperty]
    public partial string ComparisonText { get; private set; } = "Noch kein Vorjahresvergleich";

    [ObservableProperty]
    public partial string ComparisonColor { get; private set; } = MutedColor;

    [ObservableProperty]
    public partial string ActiveDaysText { get; private set; } = "0 Tage";

    [ObservableProperty]
    public partial string GamesPlayedText { get; private set; } = "0 Spiele";

    [ObservableProperty]
    public partial string SessionCountText { get; private set; } = "0 Sessions";

    [ObservableProperty]
    public partial string MostActiveMonthText { get; private set; } = "Noch kein aktiver Monat";

    [ObservableProperty]
    public partial string MostActiveMonthDetailText { get; private set; } = "Spielzeit erscheint hier.";

    [ObservableProperty]
    public partial string LongestSessionText { get; private set; } = "Noch keine Session";

    [ObservableProperty]
    public partial string LongestSessionDetailText { get; private set; } = "Dein längster Spieleabend erscheint hier.";

    [ObservableProperty]
    public partial string ChartMaximumText { get; private set; } = "1 h";

    [ObservableProperty]
    public partial string ShareFooterText { get; private set; } = "YFTimeTracker · Jahresrückblick";

    public string StatusMessage { get => statusMessage; internal set => SetProperty(ref statusMessage, value); }

    [ObservableProperty]
    public partial Visibility DataVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EmptyVisibility { get; private set; } = Visibility.Visible;

    [ObservableProperty]
    public partial bool IsShareEnabled { get; private set; }

    [ObservableProperty]
    public partial bool IsExportFolderAvailable { get; private set; }

    internal void SetExportedFile(string path)
    {
        lastExportedFilePath = path;
        IsExportFolderAvailable = true;
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        if (lastExportedFilePath is not null)
        {
            explorerService.RevealFile(lastExportedFilePath);
        }
    }

    public async Task InitializeAsync()
    {
        if (!initialized)
        {
            var availableYears = await reviews.GetAvailableYearsAsync(
                TimeZoneInfo.Local,
                CancellationToken.None);
            var currentYear = TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Year;
            var years = availableYears.Count == 0 ? [currentYear] : availableYears;
            Years.Clear();
            foreach (var year in years)
            {
                Years.Add(new YearReviewYearOption(year, year.ToString(GermanCulture)));
            }

            selectedYear = Years.FirstOrDefault(option => option.Year == currentYear) ?? Years.First();
            OnPropertyChanged(nameof(SelectedYear));
            initialized = true;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (SelectedYear is not { } yearOption)
        {
            return;
        }

        var currentRefresh = Interlocked.Increment(ref refreshVersion);
        try
        {
            var report = await reviews.GetYearReviewAsync(
                yearOption.Year,
                TimeZoneInfo.Local,
                CancellationToken.None);
            var visibleGames = report.Games.Take(8).ToArray();
            var iconPaths = gameIcons is null
                ? new string?[visibleGames.Length]
                : await Task.WhenAll(visibleGames.Select(game => gameIcons.GetGameImagePathAsync(
                    game.GameId,
                    game.ExecutablePath,
                    CancellationToken.None)));
            if (currentRefresh != Volatile.Read(ref refreshVersion))
            {
                return;
            }

            ApplyReport(report, visibleGames, iconPaths);
            StatusMessage = report.TotalDuration > TimeSpan.Zero
                ? $"Rückblick {report.Year} aktualisiert"
                : $"Für {report.Year} ist noch keine Spielzeit erfasst";
        }
        catch (Exception exception)
        {
            if (currentRefresh == Volatile.Read(ref refreshVersion))
            {
                StatusMessage = $"Jahresrückblick konnte nicht geladen werden: {exception.Message}";
            }
        }
    }

    private void ApplyReport(
        YearReview report,
        IReadOnlyList<YearReviewGame> visibleGames,
        IReadOnlyList<string?> iconPaths)
    {
        YearTitle = $"DEIN JAHR {report.Year}";
        YearSubtitle = $"1. Januar bis 31. Dezember {report.Year}";
        TotalPlaytimeText = TimeFormatter.Format(report.TotalDuration);
        (ComparisonText, ComparisonColor) = FormatComparison(
            report.TotalDuration,
            report.PreviousYearDuration,
            report.Year - 1);
        ActiveDaysText = FormatCount(report.ActiveDayCount, "Tag", "Tage");
        GamesPlayedText = FormatCount(report.GamesPlayedCount, "Spiel", "Spiele");
        SessionCountText = FormatCount(report.SessionCount, "Session", "Sessions");

        if (report.MostActiveMonth is { } activeMonth)
        {
            MostActiveMonthText = GetMonthName(activeMonth.Month, abbreviated: false);
            MostActiveMonthDetailText = $"{TimeFormatter.Format(activeMonth.Duration)} Spielzeit";
        }
        else
        {
            MostActiveMonthText = "Noch kein aktiver Monat";
            MostActiveMonthDetailText = "Spielzeit erscheint hier.";
        }

        if (report.LongestSessionGameName is { } longestGame
            && report.LongestSessionStartedAtUtc is { } longestStart)
        {
            LongestSessionText = TimeFormatter.Format(report.LongestSessionDuration);
            LongestSessionDetailText = $"{longestGame} · {TimeZoneInfo.ConvertTime(longestStart, TimeZoneInfo.Local):dd.MM.yyyy}";
        }
        else
        {
            LongestSessionText = "Noch keine Session";
            LongestSessionDetailText = "Dein längster Spieleabend erscheint hier.";
        }

        UpdateMonths(report.Months, report.MostActiveMonth?.Month);
        UpdateGames(report.TotalDuration, visibleGames, iconPaths);
        var hasData = report.TotalDuration > TimeSpan.Zero;
        DataVisibility = hasData ? Visibility.Visible : Visibility.Collapsed;
        EmptyVisibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        IsShareEnabled = hasData;
        ShareFooterText = $"YFTimeTracker · Jahresrückblick {report.Year}";
    }

    private void UpdateMonths(IReadOnlyList<YearReviewMonth> months, int? mostActiveMonth)
    {
        Months.Clear();
        var maximumHours = Math.Max(1, Math.Ceiling(months.Max(month => month.Duration.TotalHours)));
        ChartMaximumText = $"{maximumHours:0} h";
        foreach (var month in months)
        {
            var height = month.Duration <= TimeSpan.Zero
                ? 4
                : Math.Max(12, month.Duration.TotalHours / maximumHours * 156);
            Months.Add(new YearReviewMonthItemViewModel(
                GetMonthName(month.Month, abbreviated: true),
                TimeFormatter.Format(month.Duration),
                height,
                month.Month == mostActiveMonth ? "#FF6A2B" : "#77736A"));
        }
    }

    private void UpdateGames(
        TimeSpan totalDuration,
        IReadOnlyList<YearReviewGame> games,
        IReadOnlyList<string?> iconPaths)
    {
        TopGames.Clear();
        var maximumSeconds = games.Count == 0 ? 0 : games.Max(game => game.Duration.TotalSeconds);
        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            var share = totalDuration <= TimeSpan.Zero
                ? 0
                : game.Duration.TotalSeconds / totalDuration.TotalSeconds * 100;
            TopGames.Add(new YearReviewGameItemViewModel(
                index + 1,
                game.GameId,
                game.Name,
                GetInitials(game.Name),
                iconPaths[index],
                GameSourceNames.Label(game.Source),
                TimeFormatter.Format(game.Duration),
                $"{share:0.#} %",
                FormatCount(game.SessionCount, "Session", "Sessions"),
                maximumSeconds <= 0 ? 0 : game.Duration.TotalSeconds / maximumSeconds * 100));
        }
    }

    private static (string Text, string Color) FormatComparison(
        TimeSpan current,
        TimeSpan previous,
        int previousYear)
    {
        if (previous <= TimeSpan.Zero)
        {
            return current <= TimeSpan.Zero
                ? ($"Keine Spielzeit im Vergleich zu {previousYear}", MutedColor)
                : ($"Erste erfasste Spielzeit seit {previousYear}", GreenColor);
        }

        var change = (current.TotalSeconds - previous.TotalSeconds) / previous.TotalSeconds * 100;
        var prefix = change > 0 ? "+" : string.Empty;
        var color = change > 0 ? GreenColor : change < 0 ? RedColor : MutedColor;
        return ($"{prefix}{change:0} % gegenüber {previousYear}", color);
    }

    private static string GetMonthName(int month, bool abbreviated)
    {
        var name = abbreviated
            ? GermanCulture.DateTimeFormat.GetAbbreviatedMonthName(month).TrimEnd('.')
            : GermanCulture.DateTimeFormat.GetMonthName(month);
        return GermanCulture.TextInfo.ToTitleCase(name);
    }

    private static string FormatCount(int count, string singular, string plural) =>
        $"{count:N0} {(count == 1 ? singular : plural)}";

    private static string GetInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0
            ? "?"
            : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}

public sealed record YearReviewYearOption(int Year, string Label);

public sealed record YearReviewMonthItemViewModel(
    string Month,
    string DurationText,
    double BarHeight,
    string BarColor);

public sealed record YearReviewGameItemViewModel(
    int Rank,
    long GameId,
    string Name,
    string Initials,
    string? IconPath,
    string Source,
    string DurationText,
    string ShareText,
    string SessionText,
    double Progress);
