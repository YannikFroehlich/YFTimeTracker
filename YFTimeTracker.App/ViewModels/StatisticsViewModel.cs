using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.App.ViewModels;

public sealed class StatisticsViewModel : ObservableObject
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private const string BlueColor = "#387BFF";
    private const string CyanColor = "#2CE5F3";
    private const string PurpleColor = "#8A4DFF";
    private const string GreenColor = "#29E7A4";
    private const string MutedColor = "#8391A8";
    private const string RedColor = "#FF6B7A";
    private const double TimelineChartHeight = 160;
    private const double TimelineItemSpacing = 8;
    private const double DonutCenter = 70;
    private const double DonutRadius = 50;
    private const double DonutGapDegrees = 3;
    private const int DonutTopGameCount = 5;
    private const double HeatmapWeekWidth = 19;

    private readonly IPlaytimeStatisticsService statistics;
    private readonly IClock clock;
    private readonly IFilePickerService filePicker;
    private readonly IExplorerService explorerService;
    private StatisticsPeriodOption selectedPeriod;
    private int selectedCalendarYear;
    private PlaytimeStatistics? lastReport;
    private string? lastExportedFilePath;
    private int refreshVersion;
    private int calendarRefreshVersion;
    private string periodDescription = "Die letzten 30 Tage";
    private string totalDurationText = "0 min";
    private string sessionCountText = "Keine Sessions";
    private string averageDurationText = "0 min";
    private string gamesPlayedText = "Keine Spiele";
    private string comparisonText = "Keine Änderung zur Vorperiode";
    private string comparisonColor = MutedColor;
    private string chartMaximumText = "2 h";
    private string chartMiddleText = "1 h";
    private double chartMinimumWidth = 560;
    private string topGameText = "Noch keine Daten";
    private string topGameDetailText = "Starte ein Spiel, um dein Ranking aufzubauen.";
    private string favoriteWeekdayText = "Noch keine Daten";
    private string favoriteWeekdayDetailText = "Dein aktivster Wochentag erscheint hier.";
    private string longestSessionText = "0 min";
    private string longestSessionDetailText = "Noch keine abgeschlossene Spielzeit";
    private string statusMessage = "Statistiken werden aus deinen lokalen Sessions berechnet.";
    private Visibility dataVisibility = Visibility.Collapsed;
    private Visibility emptyVisibility = Visibility.Visible;
    private Visibility tagSharesVisibility = Visibility.Collapsed;
    private IReadOnlyList<Point> trendLinePoints = [];
    private IReadOnlyList<Point> trendAreaPoints = [];
    private string topGameShareText = "–";
    private bool isExportEnabled;
    private bool isExportFolderAvailable;
    private string calendarDescriptionText = "Tägliche Spielzeit im Kalenderjahr";
    private string calendarSummaryText = "Keine aktiven Tage";
    private string calendarPeakText = "Noch kein aktivster Tag";
    private string calendarStreakText = "Noch keine Serie";
    private IReadOnlyList<int> calendarYears;

    public StatisticsViewModel(
        IPlaytimeStatisticsService statistics,
        IClock clock,
        IFilePickerService filePicker,
        IExplorerService explorerService)
    {
        this.statistics = statistics;
        this.clock = clock;
        this.filePicker = filePicker;
        this.explorerService = explorerService;
        Periods =
        [
            new StatisticsPeriodOption(StatisticsPeriodKind.Last7Days, "7 Tage"),
            new StatisticsPeriodOption(StatisticsPeriodKind.Last30Days, "30 Tage"),
            new StatisticsPeriodOption(StatisticsPeriodKind.Last12Months, "12 Monate"),
            new StatisticsPeriodOption(StatisticsPeriodKind.AllTime, "Gesamte Zeit")
        ];
        selectedPeriod = Periods[1];
        selectedCalendarYear = TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Year;
        calendarYears = [selectedCalendarYear];
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync);
        OpenExportFolderCommand = new RelayCommand(OpenExportFolder);
    }

    public IReadOnlyList<StatisticsPeriodOption> Periods { get; }

    public StatisticsPeriodOption SelectedPeriod
    {
        get => selectedPeriod;
        set
        {
            if (value is not null && SetProperty(ref selectedPeriod, value))
            {
                _ = RefreshAsync();
            }
        }
    }

    public string PeriodDescription { get => periodDescription; private set => SetProperty(ref periodDescription, value); }

    public int SelectedCalendarYear
    {
        get => selectedCalendarYear;
        set
        {
            if (value > 0 && SetProperty(ref selectedCalendarYear, value))
            {
                _ = RefreshCalendarAsync(value);
            }
        }
    }

    public string TotalDurationText { get => totalDurationText; private set => SetProperty(ref totalDurationText, value); }

    public string SessionCountText { get => sessionCountText; private set => SetProperty(ref sessionCountText, value); }

    public string AverageDurationText { get => averageDurationText; private set => SetProperty(ref averageDurationText, value); }

    public string GamesPlayedText { get => gamesPlayedText; private set => SetProperty(ref gamesPlayedText, value); }

    public string ComparisonText { get => comparisonText; private set => SetProperty(ref comparisonText, value); }

    public string ComparisonColor { get => comparisonColor; private set => SetProperty(ref comparisonColor, value); }

    public string ChartMaximumText { get => chartMaximumText; private set => SetProperty(ref chartMaximumText, value); }

    public string ChartMiddleText { get => chartMiddleText; private set => SetProperty(ref chartMiddleText, value); }

    public double ChartMinimumWidth { get => chartMinimumWidth; private set => SetProperty(ref chartMinimumWidth, value); }

    public string TopGameText { get => topGameText; private set => SetProperty(ref topGameText, value); }

    public string TopGameDetailText { get => topGameDetailText; private set => SetProperty(ref topGameDetailText, value); }

    public string FavoriteWeekdayText { get => favoriteWeekdayText; private set => SetProperty(ref favoriteWeekdayText, value); }

    public string FavoriteWeekdayDetailText { get => favoriteWeekdayDetailText; private set => SetProperty(ref favoriteWeekdayDetailText, value); }

    public string LongestSessionText { get => longestSessionText; private set => SetProperty(ref longestSessionText, value); }

    public string LongestSessionDetailText { get => longestSessionDetailText; private set => SetProperty(ref longestSessionDetailText, value); }

    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }

    public Visibility DataVisibility { get => dataVisibility; private set => SetProperty(ref dataVisibility, value); }

    public Visibility EmptyVisibility { get => emptyVisibility; private set => SetProperty(ref emptyVisibility, value); }

    public Visibility TagSharesVisibility { get => tagSharesVisibility; private set => SetProperty(ref tagSharesVisibility, value); }

    public IReadOnlyList<Point> TrendLinePoints { get => trendLinePoints; private set => SetProperty(ref trendLinePoints, value); }

    public IReadOnlyList<Point> TrendAreaPoints { get => trendAreaPoints; private set => SetProperty(ref trendAreaPoints, value); }

    public string TopGameShareText { get => topGameShareText; private set => SetProperty(ref topGameShareText, value); }

    public bool IsExportEnabled { get => isExportEnabled; private set => SetProperty(ref isExportEnabled, value); }

    public bool IsExportFolderAvailable { get => isExportFolderAvailable; private set => SetProperty(ref isExportFolderAvailable, value); }

    public string CalendarDescriptionText { get => calendarDescriptionText; private set => SetProperty(ref calendarDescriptionText, value); }

    public string CalendarSummaryText { get => calendarSummaryText; private set => SetProperty(ref calendarSummaryText, value); }

    public string CalendarPeakText { get => calendarPeakText; private set => SetProperty(ref calendarPeakText, value); }

    public string CalendarStreakText { get => calendarStreakText; private set => SetProperty(ref calendarStreakText, value); }

    public ObservableCollection<StatisticsTrendPointViewModel> Timeline { get; } = [];

    public ObservableCollection<TopGameStatisticsViewModel> TopGames { get; } = [];

    public ObservableCollection<GameShareSliceViewModel> GameShares { get; } = [];

    public ObservableCollection<GameShareSliceViewModel> TagShares { get; } = [];

    public ObservableCollection<WeekdayStatisticsViewModel> Weekdays { get; } = [];

    public ObservableCollection<HeatmapWeekViewModel> HeatmapWeeks { get; } = [];

    public ObservableCollection<HeatmapMonthViewModel> HeatmapMonths { get; } = [];

    public IReadOnlyList<int> CalendarYears { get => calendarYears; private set => SetProperty(ref calendarYears, value); }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ExportCsvCommand { get; }

    public IRelayCommand OpenExportFolderCommand { get; }

    public async Task RefreshAsync()
    {
        var requestedVersion = Interlocked.Increment(ref refreshVersion);
        var requestedCalendarVersion = Interlocked.Increment(ref calendarRefreshVersion);
        var requestedCalendarYear = SelectedCalendarYear;
        StatusMessage = "Statistiken werden aktualisiert …";

        try
        {
            var reportTask = statistics.GetStatisticsAsync(
                SelectedPeriod.Kind,
                TimeZoneInfo.Local,
                CancellationToken.None);
            var calendarTask = statistics.GetCalendarHeatmapAsync(
                requestedCalendarYear,
                TimeZoneInfo.Local,
                CancellationToken.None);
            await Task.WhenAll(reportTask, calendarTask);

            var report = await reportTask;
            if (requestedVersion != Volatile.Read(ref refreshVersion))
            {
                return;
            }

            ApplyReport(report);
            if (requestedCalendarVersion == Volatile.Read(ref calendarRefreshVersion))
            {
                UpdateCalendarHeatmap(await calendarTask);
            }

            StatusMessage = report.SessionCount == 0
                ? "Noch keine Sessions im ausgewählten Zeitraum."
                : $"Zuletzt aktualisiert um {TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local):HH:mm}.";
        }
        catch (Exception exception)
        {
            if (requestedVersion == Volatile.Read(ref refreshVersion))
            {
                StatusMessage = $"Statistiken konnten nicht geladen werden: {exception.Message}";
            }
        }
    }

    private async Task RefreshCalendarAsync(int year)
    {
        var requestedVersion = Interlocked.Increment(ref calendarRefreshVersion);
        try
        {
            var calendar = await statistics.GetCalendarHeatmapAsync(
                year,
                TimeZoneInfo.Local,
                CancellationToken.None);
            if (requestedVersion == Volatile.Read(ref calendarRefreshVersion))
            {
                UpdateCalendarHeatmap(calendar);
            }
        }
        catch (Exception exception)
        {
            if (requestedVersion == Volatile.Read(ref calendarRefreshVersion))
            {
                StatusMessage = $"Kalender konnte nicht geladen werden: {exception.Message}";
            }
        }
    }

    private async Task ExportCsvAsync()
    {
        if (lastReport is not { Games.Count: > 0 } report)
        {
            return;
        }

        var path = await filePicker.PickStatisticsExportAsync(SelectedPeriod.Label, CancellationToken.None);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, BuildGamesCsv(report), new UTF8Encoding(true));
            StatusMessage = $"Statistik als CSV exportiert: {Path.GetFileName(path)}";
            lastExportedFilePath = path;
            IsExportFolderAvailable = true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"CSV-Export fehlgeschlagen: {exception.Message}";
        }
    }

    private void OpenExportFolder()
    {
        if (lastExportedFilePath is not null)
        {
            explorerService.RevealFile(lastExportedFilePath);
        }
    }

    private static string BuildGamesCsv(PlaytimeStatistics report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Rang;Spiel;Quelle;Spielzeit;Spielzeit (Stunden);Anteil (%);Sessions;Zuletzt gespielt");
        for (var index = 0; index < report.Games.Count; index++)
        {
            var game = report.Games[index];
            var share = report.TotalDuration <= TimeSpan.Zero
                ? 0
                : game.Duration.TotalSeconds / report.TotalDuration.TotalSeconds * 100;
            builder.AppendLine(string.Join(';',
                (index + 1).ToString(GermanCulture),
                CsvField(game.Name),
                CsvField(FormatSource(game.Source)),
                CsvField(TimeFormatter.Format(game.Duration)),
                game.Duration.TotalHours.ToString("0.00", GermanCulture),
                share.ToString("0.0", GermanCulture),
                game.SessionCount.ToString(GermanCulture),
                TimeZoneInfo.ConvertTime(game.LastPlayedAtUtc, TimeZoneInfo.Local).ToString("dd.MM.yyyy", GermanCulture)));
        }

        return builder.ToString();
    }

    private static string CsvField(string value)
    {
        return value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private void ApplyReport(PlaytimeStatistics report)
    {
        lastReport = report;
        IsExportEnabled = report.Games.Count > 0;
        PeriodDescription = FormatPeriodDescription(report);
        TotalDurationText = TimeFormatter.Format(report.TotalDuration);
        SessionCountText = FormatCount(report.SessionCount, "Session", "Sessions", "Keine Sessions");
        AverageDurationText = TimeFormatter.Format(report.AverageSessionDuration);
        GamesPlayedText = FormatCount(report.GamesPlayedCount, "Spiel", "Spiele", "Keine Spiele");
        (ComparisonText, ComparisonColor) = FormatComparison(report.TotalDuration, report.PreviousPeriodDuration);

        UpdateTimeline(report);
        UpdateGames(report);
        UpdateGameShares(report);
        UpdateTagShares(report);
        UpdateWeekdays(report);
        UpdateInsights(report);

        DataVisibility = report.SessionCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyVisibility = report.SessionCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTimeline(PlaytimeStatistics report)
    {
        Timeline.Clear();
        var maximumHours = Math.Max(2, Math.Ceiling(report.Timeline.Count == 0
            ? 0
            : report.Timeline.Max(point => point.Duration.TotalHours)));
        ChartMaximumText = $"{maximumHours:0} h";
        ChartMiddleText = $"{maximumHours / 2:0.#} h";
        var itemWidth = report.Period switch
        {
            StatisticsPeriodKind.Last7Days => 72d,
            StatisticsPeriodKind.Last30Days => 38d,
            _ => 62d
        };
        ChartMinimumWidth = Math.Max(560, report.Timeline.Count * (itemWidth + 8));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);
        var orderedTimeline = report.Timeline.Reverse().ToList();

        var linePoints = new List<Point>(orderedTimeline.Count);
        for (var index = 0; index < orderedTimeline.Count; index++)
        {
            var point = orderedTimeline[index];
            var height = point.Duration <= TimeSpan.Zero
                ? 4
                : Math.Max(10, point.Duration.TotalHours / maximumHours * 152);
            var containsToday = point.StartDate <= today && point.EndDateExclusive > today;
            var color = containsToday ? CyanColor : index % 3 == 2 ? PurpleColor : BlueColor;
            Timeline.Add(new StatisticsTrendPointViewModel(
                FormatTimelineLabel(point, report.Period),
                TimeFormatter.Format(point.Duration),
                FormatTimelineTooltip(point),
                height,
                itemWidth,
                Math.Max(14, itemWidth - 14),
                color));

            linePoints.Add(new Point(
                index * (itemWidth + TimelineItemSpacing) + itemWidth / 2,
                TimelineChartHeight - height));
        }

        TrendLinePoints = linePoints;
        TrendAreaPoints = BuildTrendAreaPoints(linePoints);
    }

    private static List<Point> BuildTrendAreaPoints(List<Point> linePoints)
    {
        var areaPoints = new List<Point>(linePoints.Count + 2);
        if (linePoints.Count == 0)
        {
            return areaPoints;
        }

        var firstX = linePoints[0].X;
        var lastX = linePoints[^1].X;
        areaPoints.Add(new Point(firstX, TimelineChartHeight));
        areaPoints.AddRange(linePoints);
        areaPoints.Add(new Point(lastX, TimelineChartHeight));
        return areaPoints;
    }

    private void UpdateGames(PlaytimeStatistics report)
    {
        TopGames.Clear();
        var maximumSeconds = report.Games.Count == 0 ? 0 : report.Games.Max(game => game.Duration.TotalSeconds);

        for (var index = 0; index < Math.Min(8, report.Games.Count); index++)
        {
            var game = report.Games[index];
            var share = report.TotalDuration <= TimeSpan.Zero
                ? 0
                : game.Duration.TotalSeconds / report.TotalDuration.TotalSeconds * 100;
            TopGames.Add(new TopGameStatisticsViewModel(
                $"{index + 1}",
                game.Name,
                GetInitials(game.Name),
                FormatSource(game.Source),
                TimeFormatter.Format(game.Duration),
                $"{share:0.#} %",
                FormatCount(game.SessionCount, "Session", "Sessions", "Keine Sessions"),
                $"Zuletzt {TimeZoneInfo.ConvertTime(game.LastPlayedAtUtc, TimeZoneInfo.Local):dd.MM.yyyy}",
                maximumSeconds <= 0 ? 0 : game.Duration.TotalSeconds / maximumSeconds * 100,
                GetAccentColor(index)));
        }
    }

    private void UpdateGameShares(PlaytimeStatistics report)
    {
        GameShares.Clear();
        if (report.TotalDuration <= TimeSpan.Zero || report.Games.Count == 0)
        {
            TopGameShareText = "–";
            return;
        }

        TopGameShareText = $"{report.Games[0].Duration.TotalSeconds / report.TotalDuration.TotalSeconds * 100:0.#} %";

        var topGames = report.Games.Take(DonutTopGameCount).ToArray();
        var otherDuration = report.Games.Skip(DonutTopGameCount)
            .Aggregate(TimeSpan.Zero, (sum, game) => sum + game.Duration);

        var slices = topGames
            .Select((game, index) => (game.Name, game.Duration, Color: GetAccentColor(index)))
            .ToList();
        if (otherDuration > TimeSpan.Zero)
        {
            slices.Add(("Sonstige", otherDuration, MutedColor));
        }

        foreach (var slice in BuildShareSlices(slices, report.TotalDuration.TotalSeconds))
        {
            GameShares.Add(slice);
        }
    }

    // Ein Spiel kann mehreren Tags zugeordnet sein und zählt dann in jedem seiner Buckets voll
    // mit - die Anteile beziehen sich deshalb bewusst auf die Summe aller Bucket-Dauern, nicht auf
    // die Gesamtspielzeit der Seite, sonst würden die Bogenstücke nicht mehr exakt 360°/100 % ergeben.
    private void UpdateTagShares(PlaytimeStatistics report)
    {
        TagShares.Clear();

        var tagDurations = new List<(string Name, TimeSpan Duration)>();
        var untaggedDuration = TimeSpan.Zero;
        foreach (var game in report.Games)
        {
            if (game.Tags.Count == 0)
            {
                untaggedDuration += game.Duration;
                continue;
            }

            foreach (var tag in game.Tags)
            {
                var index = tagDurations.FindIndex(entry => string.Equals(entry.Name, tag, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    tagDurations[index] = (tagDurations[index].Name, tagDurations[index].Duration + game.Duration);
                }
                else
                {
                    tagDurations.Add((tag, game.Duration));
                }
            }
        }

        if (untaggedDuration > TimeSpan.Zero)
        {
            tagDurations.Add(("Ohne Tag", untaggedDuration));
        }

        var totalTaggedSeconds = tagDurations.Sum(entry => entry.Duration.TotalSeconds);
        TagSharesVisibility = totalTaggedSeconds > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (totalTaggedSeconds <= 0)
        {
            return;
        }

        var slices = tagDurations
            .OrderByDescending(entry => entry.Duration)
            .Select((entry, index) => (
                entry.Name,
                entry.Duration,
                Color: entry.Name == "Ohne Tag" ? MutedColor : GetAccentColor(index)))
            .ToList();

        foreach (var slice in BuildShareSlices(slices, totalTaggedSeconds))
        {
            TagShares.Add(slice);
        }
    }

    private static IEnumerable<GameShareSliceViewModel> BuildShareSlices(
        IReadOnlyList<(string Name, TimeSpan Duration, string Color)> slices,
        double totalSecondsForShare)
    {
        var cumulativeDegrees = 0d;
        var halfGapDegrees = slices.Count > 1 ? DonutGapDegrees / 2 : 0;
        foreach (var slice in slices)
        {
            var shareFraction = totalSecondsForShare <= 0 ? 0 : slice.Duration.TotalSeconds / totalSecondsForShare;
            var sliceDegrees = shareFraction * 360;
            var startDegrees = cumulativeDegrees + halfGapDegrees;
            var endDegrees = cumulativeDegrees + sliceDegrees - halfGapDegrees;
            cumulativeDegrees += sliceDegrees;

            if (endDegrees <= startDegrees)
            {
                continue;
            }

            var span = Math.Min(endDegrees - startDegrees, 359.99);
            yield return new GameShareSliceViewModel(
                PointOnDonut(startDegrees),
                PointOnDonut(startDegrees + span),
                DonutRadius,
                span > 180,
                slice.Color,
                slice.Name,
                $"{shareFraction * 100:0.#} %");
        }
    }

    private static Point PointOnDonut(double angleDegrees)
    {
        var angleRadians = (angleDegrees - 90) * Math.PI / 180;
        return new Point(
            DonutCenter + DonutRadius * Math.Cos(angleRadians),
            DonutCenter + DonutRadius * Math.Sin(angleRadians));
    }

    private void UpdateCalendarHeatmap(CalendarHeatmapStatistics calendar)
    {
        HeatmapWeeks.Clear();
        HeatmapMonths.Clear();
        CalendarYears = calendar.AvailableYears;

        CalendarDescriptionText = $"Tägliche Spielzeit im Kalenderjahr {calendar.Year}";
        var yearStart = new DateOnly(calendar.Year, 1, 1);
        var yearEndExclusive = yearStart.AddYears(1);
        var gridStart = PlaytimeStatisticsService.GetIsoWeekStart(yearStart);
        var gridEndExclusive = PlaytimeStatisticsService.GetIsoWeekStart(yearEndExclusive.AddDays(-1)).AddDays(7);
        var daysByDate = calendar.Days.ToDictionary(day => day.Date);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);

        for (var weekStart = gridStart; weekStart < gridEndExclusive; weekStart = weekStart.AddDays(7))
        {
            var week = new HeatmapWeekViewModel();
            for (var offset = 0; offset < 7; offset++)
            {
                var date = weekStart.AddDays(offset);
                if (date < yearStart || date >= yearEndExclusive)
                {
                    week.Days.Add(new HeatmapDayViewModel("#00000000", string.Empty, 0));
                    continue;
                }

                var duration = daysByDate.GetValueOrDefault(date)?.Duration ?? TimeSpan.Zero;
                var isFuture = date > today;
                var level = isFuture ? -1 : GetHeatmapLevel(duration);
                var tooltip = isFuture
                    ? $"{date:dd.MM.yyyy}: noch nicht erreicht"
                    : $"{date:dd.MM.yyyy}: {TimeFormatter.Format(duration)}";
                week.Days.Add(new HeatmapDayViewModel(GetHeatmapColor(level), tooltip, 1));
            }

            HeatmapWeeks.Add(week);
        }

        UpdateHeatmapMonths(gridStart, gridEndExclusive, calendar.Year);
        UpdateCalendarSummary(calendar.Days.Where(day => day.Date <= today).ToArray());
    }

    private static int GetHeatmapLevel(TimeSpan duration) => duration.TotalHours switch
    {
        >= 4 => 4,
        >= 2 => 3,
        >= 1 => 2,
        > 0 => 1,
        _ => 0
    };

    private static string GetHeatmapColor(int level) => level switch
    {
        4 => BlueColor,
        3 => "#99387BFF",
        2 => "#66387BFF",
        1 => "#33387BFF",
        -1 => "#148391A8",
        _ => "#1F9AA8BF"
    };

    private void UpdateHeatmapMonths(DateOnly gridStart, DateOnly gridEndExclusive, int year)
    {
        int? currentMonth = null;
        var weekCount = 0;
        for (var weekStart = gridStart; weekStart < gridEndExclusive; weekStart = weekStart.AddDays(7))
        {
            var midpoint = weekStart.AddDays(3);
            var month = midpoint.Year < year ? 1 : midpoint.Year > year ? 12 : midpoint.Month;
            if (currentMonth == month)
            {
                weekCount++;
                continue;
            }

            if (currentMonth is { } completedMonth)
            {
                AddHeatmapMonth(completedMonth, weekCount);
            }

            currentMonth = month;
            weekCount = 1;
        }

        if (currentMonth is { } lastMonth)
        {
            AddHeatmapMonth(lastMonth, weekCount);
        }
    }

    private void AddHeatmapMonth(int month, int weekCount)
    {
        var label = GermanCulture.DateTimeFormat.GetAbbreviatedMonthName(month).TrimEnd('.');
        HeatmapMonths.Add(new HeatmapMonthViewModel(label, weekCount * HeatmapWeekWidth));
    }

    private void UpdateCalendarSummary(IReadOnlyList<DailyPlaytimeInfo> elapsedDays)
    {
        var activeDays = elapsedDays.Where(day => day.Duration > TimeSpan.Zero).ToArray();
        var totalDuration = TimeSpan.FromTicks(activeDays.Sum(day => day.Duration.Ticks));
        CalendarSummaryText = activeDays.Length == 0
            ? "Keine aktiven Tage"
            : $"{activeDays.Length} {(activeDays.Length == 1 ? "aktiver Tag" : "aktive Tage")} · {TimeFormatter.Format(totalDuration)}";

        var peakDay = activeDays
            .OrderByDescending(day => day.Duration)
            .ThenBy(day => day.Date)
            .FirstOrDefault();
        CalendarPeakText = peakDay is null
            ? "Noch kein aktivster Tag"
            : $"Aktivster Tag: {peakDay.Date:dd.MM.} · {TimeFormatter.Format(peakDay.Duration)}";

        var longestStreak = 0;
        var currentStreak = 0;
        foreach (var day in elapsedDays.OrderBy(day => day.Date))
        {
            currentStreak = day.Duration > TimeSpan.Zero ? currentStreak + 1 : 0;
            longestStreak = Math.Max(longestStreak, currentStreak);
        }

        CalendarStreakText = longestStreak == 0
            ? "Noch keine Serie"
            : $"Längste Serie: {longestStreak} {(longestStreak == 1 ? "Tag" : "Tage")}";
    }

    private void UpdateWeekdays(PlaytimeStatistics report)
    {
        Weekdays.Clear();
        var maximumSeconds = report.Weekdays.Count == 0 ? 0 : report.Weekdays.Max(day => day.Duration.TotalSeconds);
        foreach (var day in report.Weekdays)
        {
            Weekdays.Add(new WeekdayStatisticsViewModel(
                GermanCulture.DateTimeFormat.GetDayName(day.DayOfWeek),
                TimeFormatter.Format(day.Duration),
                maximumSeconds <= 0 ? 0 : day.Duration.TotalSeconds / maximumSeconds * 100,
                day.Duration == report.Weekdays.Max(candidate => candidate.Duration) && day.Duration > TimeSpan.Zero
                    ? CyanColor
                    : BlueColor));
        }
    }

    private void UpdateInsights(PlaytimeStatistics report)
    {
        var topGame = report.Games.FirstOrDefault();
        TopGameText = topGame?.Name ?? "Noch keine Daten";
        TopGameDetailText = topGame is null
            ? "Starte ein Spiel, um dein Ranking aufzubauen."
            : $"{TimeFormatter.Format(topGame.Duration)} · {FormatCount(topGame.SessionCount, "Session", "Sessions", "Keine Sessions")}";

        var favoriteDay = report.Weekdays.OrderByDescending(day => day.Duration).FirstOrDefault();
        FavoriteWeekdayText = favoriteDay is null || favoriteDay.Duration <= TimeSpan.Zero
            ? "Noch keine Daten"
            : GermanCulture.DateTimeFormat.GetDayName(favoriteDay.DayOfWeek);
        FavoriteWeekdayDetailText = favoriteDay is null || favoriteDay.Duration <= TimeSpan.Zero
            ? "Dein aktivster Wochentag erscheint hier."
            : $"{TimeFormatter.Format(favoriteDay.Duration)} im ausgewählten Zeitraum";

        LongestSessionText = TimeFormatter.Format(report.LongestSessionDuration);
        LongestSessionDetailText = report.LongestSessionGameName is null
            ? "Noch keine erfasste Spielzeit"
            : report.LongestSessionGameName;
    }

    private static string FormatPeriodDescription(PlaytimeStatistics report)
    {
        var lastDay = report.PeriodEndExclusive.AddDays(-1);
        return report.Period switch
        {
            StatisticsPeriodKind.Last7Days or StatisticsPeriodKind.Last30Days =>
                $"{report.PeriodStart:dd.MM.yyyy} – {lastDay:dd.MM.yyyy}",
            StatisticsPeriodKind.Last12Months or StatisticsPeriodKind.AllTime when report.Timeline.FirstOrDefault()?.BucketKind == StatisticsBucketKind.Month =>
                $"{report.PeriodStart.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", GermanCulture)} – {lastDay.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", GermanCulture)}",
            _ => $"{report.PeriodStart:dd.MM.yyyy} – {lastDay:dd.MM.yyyy}"
        };
    }

    private static string FormatTimelineLabel(StatisticsTimelinePoint point, StatisticsPeriodKind period)
    {
        if (point.BucketKind == StatisticsBucketKind.Month)
        {
            return point.StartDate.ToDateTime(TimeOnly.MinValue).ToString("MMM yy", GermanCulture).TrimEnd('.');
        }

        return period == StatisticsPeriodKind.Last7Days
            ? point.StartDate.ToDateTime(TimeOnly.MinValue).ToString("ddd", GermanCulture).TrimEnd('.')
            : point.StartDate.ToString("dd.MM.");
    }

    private static string FormatTimelineTooltip(StatisticsTimelinePoint point)
    {
        var period = point.BucketKind == StatisticsBucketKind.Month
            ? point.StartDate.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", GermanCulture)
            : point.StartDate.ToString("dd.MM.yyyy");
        return $"{period}: {TimeFormatter.Format(point.Duration)}";
    }

    private static (string Text, string Color) FormatComparison(TimeSpan current, TimeSpan? previous)
    {
        if (previous is null)
        {
            return ("Gesamte aufgezeichnete Spielzeit", CyanColor);
        }

        if (previous <= TimeSpan.Zero)
        {
            return current <= TimeSpan.Zero
                ? ("Keine Änderung zur Vorperiode", MutedColor)
                : ("Neu in diesem Zeitraum", GreenColor);
        }

        var change = (current.TotalSeconds - previous.Value.TotalSeconds) / previous.Value.TotalSeconds * 100;
        var prefix = change > 0 ? "+" : string.Empty;
        return ($"{prefix}{change:0} % zur Vorperiode", change > 0 ? GreenColor : change < 0 ? RedColor : MutedColor);
    }

    private static string FormatCount(int count, string singular, string plural, string empty)
    {
        return count == 0 ? empty : $"{count} {(count == 1 ? singular : plural)}";
    }

    private static string FormatSource(GameSource source) => source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.Epic => "EPIC",
        GameSource.Gog => "GOG",
        GameSource.Xbox => "XBOX",
        GameSource.BattleNet => "BATTLE.NET",
        GameSource.Ubisoft => "UBISOFT",
        GameSource.EaApp => "EA APP",
        _ => "MANUELL"
    };

    private static string GetInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private static string GetAccentColor(int index) => (index % 3) switch
    {
        1 => PurpleColor,
        2 => CyanColor,
        _ => BlueColor
    };
}

public sealed record StatisticsPeriodOption(StatisticsPeriodKind Kind, string Label);

public sealed record StatisticsTrendPointViewModel(
    string Label,
    string DurationText,
    string TooltipText,
    double BarHeight,
    double ItemWidth,
    double BarWidth,
    string BarColor);

public sealed record TopGameStatisticsViewModel(
    string Rank,
    string Name,
    string Initials,
    string Source,
    string DurationText,
    string ShareText,
    string SessionText,
    string LastPlayedText,
    double Progress,
    string AccentColor);

public sealed record WeekdayStatisticsViewModel(
    string DayName,
    string DurationText,
    double Progress,
    string AccentColor);

public sealed record GameShareSliceViewModel(
    Point ArcStart,
    Point ArcEnd,
    double Radius,
    bool IsLargeArc,
    string StrokeColor,
    string Name,
    string ShareText);

public sealed class HeatmapWeekViewModel
{
    public ObservableCollection<HeatmapDayViewModel> Days { get; } = [];
}

public sealed record HeatmapMonthViewModel(string Label, double Width);

public sealed record HeatmapDayViewModel(string ColorHex, string TooltipText, double Opacity);
