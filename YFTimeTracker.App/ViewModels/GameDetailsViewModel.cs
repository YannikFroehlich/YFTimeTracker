using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.App.ViewModels;

public sealed partial class GameDetailsViewModel : ObservableObject
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private const string ProgressNormalColor = "#FF6A2B";
    private const string ProgressLimitReachedColor = "#FF5368";
    private readonly IGameRepository games;
    private readonly IGameCatalogService catalog;
    private readonly IGameSessionRepository sessionRepository;
    private readonly IGameSessionEditor sessionEditor;
    private readonly IClock clock;
    private readonly IGameIconService? gameIcons;
    private readonly IGameLaunchService? gameLaunchService;
    private readonly IFilePickerService? filePicker;
    private readonly IReadOnlyList<ILauncherPlaytimeReader> playtimeReaders;
    private Game? loadedGame;
    private long gameId;
    private SessionListItemViewModel? selectedSession;
    private long? editingSessionId;
    private string gameName = "Spiel";
    private bool hasCustomCover;
    private string tagsText = string.Empty;

    public GameDetailsViewModel(
        IGameRepository games,
        IGameCatalogService catalog,
        IGameSessionRepository sessionRepository,
        IGameSessionEditor sessionEditor,
        IClock clock,
        IGameIconService? gameIcons = null,
        IGameLaunchService? gameLaunchService = null,
        IFilePickerService? filePicker = null,
        IEnumerable<ILauncherPlaytimeReader>? playtimeReaders = null)
    {
        this.games = games;
        this.catalog = catalog;
        this.sessionRepository = sessionRepository;
        this.sessionEditor = sessionEditor;
        this.clock = clock;
        this.gameIcons = gameIcons;
        this.gameLaunchService = gameLaunchService;
        this.filePicker = filePicker;
        this.playtimeReaders = playtimeReaders?.ToArray() ?? [];

        ResetEditorTimes();
    }

    public ObservableCollection<GameExecutableItemViewModel> Executables { get; } = [];

    public ObservableCollection<SessionListItemViewModel> Sessions { get; } = [];

    public ObservableCollection<GameDetailTimelinePointViewModel> Timeline { get; } = [];

    public string GameName
    {
        get => gameName;
        set => SetProperty(ref gameName, value ?? string.Empty);
    }

    [ObservableProperty]
    public partial string Initials { get; private set; } = "YF";

    [ObservableProperty]
    public partial string? IconPath { get; private set; }

    public bool HasCustomCover
    {
        get => hasCustomCover;
        private set
        {
            if (SetProperty(ref hasCustomCover, value))
            {
                OnPropertyChanged(nameof(RemoveCoverVisibility));
                RemoveCoverCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Visibility RemoveCoverVisibility => HasCustomCover ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial string SourceLabel { get; private set; } = "MANUELL";

    [ObservableProperty]
    public partial string SourceDetail { get; private set; } = "Lokal hinzugefügt";

    [ObservableProperty]
    public partial string InstallDirectory { get; private set; } = "Kein Installationsordner hinterlegt";

    [ObservableProperty]
    public partial string PrimaryExecutablePath { get; private set; } = string.Empty;

    public string TagsText
    {
        get => tagsText;
        set => SetProperty(ref tagsText, value ?? string.Empty);
    }

    [ObservableProperty]
    public partial double DailyPlaytimeLimitMinutes { get; set; }

    [ObservableProperty]
    public partial double WeeklyPlaytimeLimitMinutes { get; set; }

    /// <summary>
    /// Vor der Aufzeichnung gespielte Zeit, getrennt nach Stunden und Minuten
    /// eingegeben: 300 Stunden als Minutenzahl waeren nicht zumutbar.
    /// </summary>
    [ObservableProperty]
    public partial double BaselinePlaytimeHours { get; set; }

    [ObservableProperty]
    public partial double BaselinePlaytimeMinutes { get; set; }

    public Visibility LauncherPlaytimeImportVisibility => CanImportLauncherPlaytime ? Visibility.Visible : Visibility.Collapsed;

    private bool CanImportLauncherPlaytime => PlaytimeReader is not null;

    private bool HasLoadedGame => loadedGame is not null;

    private bool CanLaunchGame => loadedGame?.PrimaryExecutable is not null;

    public string LauncherPlaytimeImportText => $"Aus {LauncherName} übernehmen";

    private ILauncherPlaytimeReader? PlaytimeReader => loadedGame is { ExternalGameId.Length: > 0 } game
        ? playtimeReaders.FirstOrDefault(reader => reader.Source == game.Source)
        : null;

    private string LauncherName => loadedGame?.Source == GameSource.Gog ? "GOG Galaxy" : "Steam";

    [ObservableProperty]
    public partial string BaselineSummaryText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility BaselineSummaryVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string DailyProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double DailyProgressPercent { get; private set; }

    [ObservableProperty]
    public partial string DailyProgressColor { get; private set; } = ProgressNormalColor;

    [ObservableProperty]
    public partial Visibility DailyProgressVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string WeeklyProgressText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double WeeklyProgressPercent { get; private set; }

    [ObservableProperty]
    public partial string WeeklyProgressColor { get; private set; } = ProgressNormalColor;

    [ObservableProperty]
    public partial Visibility WeeklyProgressVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string TotalPlaytimeText { get; private set; } = "0 min";

    [ObservableProperty]
    public partial string SessionCountText { get; private set; } = "Keine Sessions";

    [ObservableProperty]
    public partial string AverageSessionText { get; private set; } = "0 min";

    [ObservableProperty]
    public partial string LastPlayedText { get; private set; } = "Noch nicht gespielt";

    [ObservableProperty]
    public partial string ChartMaximumText { get; private set; } = "2 h";

    [ObservableProperty]
    public partial string ChartMiddleText { get; private set; } = "1 h";

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "Spieldetails werden geladen …";

    [ObservableProperty]
    public partial string SessionEditorTitle { get; private set; } = "NEUE SESSION";

    [ObservableProperty]
    public partial string SessionEditorDescription { get; private set; } = "Ergänze fehlende Spielzeit mit lokalen Start- und Endzeiten.";

    [ObservableProperty]
    public partial string SessionSaveButtonText { get; private set; } = "Session hinzufügen";

    [ObservableProperty]
    public partial DateTimeOffset EditorStartDate { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset EditorEndDate { get; set; }

    [ObservableProperty]
    public partial TimeSpan? EditorStartTime { get; set; }

    [ObservableProperty]
    public partial TimeSpan? EditorEndTime { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionCanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveSessionCommand))]
    public partial bool SessionEditorEnabled { get; private set; } = true;

    public bool SessionCanSave => SessionEditorEnabled && loadedGame is not null;

    public bool CanDeleteSelectedSession => SelectedSession?.CanModify == true;

    public SessionListItemViewModel? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (!SetProperty(ref selectedSession, value))
            {
                return;
            }

            if (value is not null)
            {
                BeginEditSession(value);
            }

            OnPropertyChanged(nameof(CanDeleteSelectedSession));
        }
    }

    [ObservableProperty]
    public partial Visibility ContentVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility ErrorVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility SessionsVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EmptySessionsVisibility { get; private set; } = Visibility.Visible;

    public async Task LoadAsync(long requestedGameId)
    {
        gameId = requestedGameId;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (gameId <= 0)
        {
            ShowError("Das angeforderte Spiel ist ungültig.");
            return;
        }

        try
        {
            var game = await games.GetByIdAsync(gameId, CancellationToken.None);
            if (game is null)
            {
                ShowError("Das Spiel wurde nicht gefunden oder inzwischen gelöscht.");
                return;
            }

            var storedSessions = await sessionRepository.GetSessionsForGameAsync(gameId, CancellationToken.None);
            var resolvedIconPath = gameIcons is null
                ? null
                : await gameIcons.GetGameImagePathAsync(
                    game.Id,
                    game.PrimaryExecutable?.ExecutablePath,
                    CancellationToken.None);
            var hasCustomCover = gameIcons is not null
                && await gameIcons.HasCustomCoverAsync(game.Id, CancellationToken.None);
            loadedGame = game;
            HasCustomCover = hasCustomCover;
            ApplyGame(game, resolvedIconPath);
            ApplySessions(storedSessions);
            UpdateLimitProgress(game, storedSessions);
            ContentVisibility = Visibility.Visible;
            ErrorVisibility = Visibility.Collapsed;
            SaveGameCommand.NotifyCanExecuteChanged();
            SaveSessionCommand.NotifyCanExecuteChanged();
            LaunchGameCommand.NotifyCanExecuteChanged();
            ImportLauncherPlaytimeCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(LauncherPlaytimeImportVisibility));
            OnPropertyChanged(nameof(LauncherPlaytimeImportText));
            StatusMessage = $"Zuletzt aktualisiert um {TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local):HH:mm}.";
        }
        catch (Exception exception)
        {
            ShowError($"Spieldetails konnten nicht geladen werden: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task ChooseCoverAsync()
    {
        if (loadedGame is null || gameIcons is null || filePicker is null)
        {
            StatusMessage = "Eigene Cover sind momentan nicht verfügbar.";
            return;
        }

        var path = await filePicker.PickGameCoverAsync(CancellationToken.None);
        if (path is null)
        {
            return;
        }

        try
        {
            IconPath = await gameIcons.SetCustomCoverAsync(loadedGame.Id, path, CancellationToken.None);
            HasCustomCover = true;
            ApplySessions(Sessions.Select(session => session.Model).ToArray());
            StatusMessage = "Eigenes Cover gespeichert";
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Cover konnte nicht gespeichert werden: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasCustomCover))]
    private async Task RemoveCoverAsync()
    {
        if (loadedGame is null || gameIcons is null)
        {
            return;
        }

        try
        {
            await gameIcons.RemoveCustomCoverAsync(loadedGame.Id, CancellationToken.None);
            IconPath = await gameIcons.GetIconPathAsync(
                loadedGame.PrimaryExecutable?.ExecutablePath,
                CancellationToken.None);
            HasCustomCover = false;
            ApplySessions(Sessions.Select(session => session.Model).ToArray());
            StatusMessage = "Eigenes Cover entfernt; das EXE-Icon wird wieder verwendet";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Cover konnte nicht entfernt werden: {exception.Message}";
        }
    }

    public void RefreshLiveDurations()
    {
        var now = clock.UtcNow;
        foreach (var session in Sessions)
        {
            session.RefreshDuration(now);
        }

        UpdateSummary(Sessions.Select(session => session.Model).ToArray());
    }

    public async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            StatusMessage = "Bitte zuerst eine Session auswählen.";
            return;
        }

        try
        {
            await sessionEditor.DeleteSessionAsync(SelectedSession.Id, CancellationToken.None);
            NewSession();
            await RefreshAsync();
            StatusMessage = "Session gelöscht";
        }
        catch (YFTimeTrackerException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Session konnte nicht gelöscht werden: {exception.Message}";
        }
    }

    private void ApplyGame(Game game, string? resolvedIconPath)
    {
        GameName = game.Name;
        Initials = GetInitials(game.Name);
        IconPath = resolvedIconPath;
        SourceLabel = GameSourceNames.Label(game.Source);
        SourceDetail = game.ExternalGameId is { Length: > 0 }
            ? $"Launcher-ID {game.ExternalGameId}"
            : game.Source == GameSource.Manual ? "Lokal hinzugefügt" : "Automatisch erkannt";
        InstallDirectory = string.IsNullOrWhiteSpace(game.InstallDirectory)
            ? "Kein Installationsordner hinterlegt"
            : game.InstallDirectory;
        PrimaryExecutablePath = game.PrimaryExecutable?.ExecutablePath ?? string.Empty;
        DailyPlaytimeLimitMinutes = game.DailyPlaytimeLimitMinutes ?? 0;
        WeeklyPlaytimeLimitMinutes = game.WeeklyPlaytimeLimitMinutes ?? 0;
        BaselinePlaytimeHours = (game.BaselinePlaytimeMinutes ?? 0) / 60;
        BaselinePlaytimeMinutes = (game.BaselinePlaytimeMinutes ?? 0) % 60;
        TagsText = string.Join(", ", game.Tags.Select(tag => tag.Tag));

        Executables.Clear();
        foreach (var executable in game.Executables
                     .OrderByDescending(executable => executable.IsPrimary)
                     .ThenBy(executable => executable.ExecutableName, StringComparer.CurrentCultureIgnoreCase))
        {
            Executables.Add(new GameExecutableItemViewModel(
                executable.ExecutableName,
                executable.ExecutablePath,
                executable.IsPrimary ? "PRIMÄR" : "ALTERNATIV",
                File.Exists(executable.ExecutablePath) ? "EXE gefunden" : "Datei derzeit nicht gefunden",
                executable.IsPrimary));
        }
    }

    private void ApplySessions(IReadOnlyList<GameSession> storedSessions)
    {
        var selectedId = SelectedSession?.Id;
        Sessions.Clear();
        foreach (var session in storedSessions.OrderByDescending(session => session.StartedAtUtc))
        {
            Sessions.Add(new SessionListItemViewModel(session, clock.UtcNow, IconPath));
        }

        selectedSession = selectedId is null
            ? null
            : Sessions.FirstOrDefault(session => session.Id == selectedId.Value);
        OnPropertyChanged(nameof(SelectedSession));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));

        SessionsVisibility = Sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptySessionsVisibility = Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary(storedSessions);
        UpdateTimeline(storedSessions);
    }

    private void UpdateSummary(IReadOnlyList<GameSession> storedSessions)
    {
        var durations = storedSessions.Select(session => session.GetEffectiveDuration(clock.UtcNow)).ToArray();
        var trackedTotal = TimeSpan.FromTicks(durations.Sum(duration => duration.Ticks));
        var baseline = TimeSpan.FromMinutes(loadedGame?.BaselinePlaytimeMinutes ?? 0);
        var total = trackedTotal + baseline;

        // Der Durchschnitt bleibt bewusst auf die aufgezeichneten Sessions bezogen:
        // die Basis-Spielzeit ist eine Summe ohne Sessions.
        var average = durations.Length == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(trackedTotal.Ticks / durations.Length);
        var lastPlayedAt = storedSessions
            .Select(session => session.EndedAtUtc ?? (session.IsOpen ? clock.UtcNow : session.LastSeenAtUtc))
            .DefaultIfEmpty()
            .Max();

        TotalPlaytimeText = TimeFormatter.Format(total);
        BaselineSummaryText = $"davon {TimeFormatter.Format(baseline)} Basis-Spielzeit";
        BaselineSummaryVisibility = baseline > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        SessionCountText = durations.Length == 0
            ? "Keine Sessions"
            : $"{durations.Length} {(durations.Length == 1 ? "Session" : "Sessions")}";
        AverageSessionText = TimeFormatter.Format(average);
        LastPlayedText = durations.Length == 0
            ? "Noch nicht gespielt"
            : storedSessions.Any(session => session.IsOpen)
                ? "Jetzt aktiv"
                : TimeZoneInfo.ConvertTime(lastPlayedAt, TimeZoneInfo.Local).ToString("dd.MM.yyyy · HH:mm", GermanCulture);
    }

    private void UpdateTimeline(IReadOnlyList<GameSession> storedSessions)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);
        var start = today.AddDays(-29);
        var durations = Enumerable.Range(0, 30)
            .Select(offset => start.AddDays(offset))
            .Select(date => new
            {
                Date = date,
                Duration = GetDurationForLocalDay(storedSessions, date)
            })
            .ToArray();
        var maximumHours = Math.Max(2, Math.Ceiling(durations.Max(item => item.Duration.TotalHours)));
        ChartMaximumText = $"{maximumHours:0} h";
        ChartMiddleText = $"{maximumHours / 2:0.#} h";

        Timeline.Clear();
        for (var index = 0; index < durations.Length; index++)
        {
            var item = durations[index];
            var height = item.Duration <= TimeSpan.Zero
                ? 4
                : Math.Max(10, item.Duration.TotalHours / maximumHours * 142);
            Timeline.Add(new GameDetailTimelinePointViewModel(
                index % 5 == 0 || item.Date == today ? item.Date.ToString("dd.MM.") : string.Empty,
                $"{item.Date:dd.MM.yyyy}: {TimeFormatter.Format(item.Duration)}",
                item.Duration,
                height,
                item.Date == today ? "#FF6A2B" : "#77736A"));
        }
    }

    private TimeSpan GetDurationForLocalDay(IEnumerable<GameSession> sessions, DateOnly date)
    {
        return SessionOverlapCalculator.GetDurationForLocalRange(
            sessions,
            date,
            date.AddDays(1),
            TimeZoneInfo.Local,
            clock.UtcNow);
    }

    private void UpdateLimitProgress(Game game, IReadOnlyList<GameSession> storedSessions)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local).Date);

        if (game.DailyPlaytimeLimitMinutes is { } dailyLimit && dailyLimit > 0)
        {
            var minutes = GetDurationForLocalDay(storedSessions, today).TotalMinutes;
            DailyProgressText = $"{minutes:0} / {dailyLimit} Min heute";
            DailyProgressPercent = Math.Clamp(minutes / dailyLimit * 100, 0, 100);
            DailyProgressColor = minutes >= dailyLimit ? ProgressLimitReachedColor : ProgressNormalColor;
            DailyProgressVisibility = Visibility.Visible;
        }
        else
        {
            DailyProgressVisibility = Visibility.Collapsed;
        }

        if (game.WeeklyPlaytimeLimitMinutes is { } weeklyLimit && weeklyLimit > 0)
        {
            var weekStart = PlaytimeStatisticsService.GetIsoWeekStart(today);
            var minutes = SessionOverlapCalculator.GetDurationForLocalRange(
                storedSessions,
                weekStart,
                weekStart.AddDays(7),
                TimeZoneInfo.Local,
                clock.UtcNow).TotalMinutes;
            WeeklyProgressText = $"{minutes:0} / {weeklyLimit} Min diese Woche";
            WeeklyProgressPercent = Math.Clamp(minutes / weeklyLimit * 100, 0, 100);
            WeeklyProgressColor = minutes >= weeklyLimit ? ProgressLimitReachedColor : ProgressNormalColor;
            WeeklyProgressVisibility = Visibility.Visible;
        }
        else
        {
            WeeklyProgressVisibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Stunden und Minuten zu einem Minutenwert. Ein leeres Feld liefert in der
    /// NumberBox NaN - das darf nicht als Spielzeit durchgehen.
    /// </summary>
    private int? GetBaselineMinutes()
    {
        var hours = double.IsNaN(BaselinePlaytimeHours) ? 0 : Math.Max(0, BaselinePlaytimeHours);
        var minutes = double.IsNaN(BaselinePlaytimeMinutes) ? 0 : Math.Max(0, BaselinePlaytimeMinutes);
        var total = (int)Math.Round(hours * 60 + minutes, MidpointRounding.AwayFromZero);
        return total > 0 ? total : null;
    }

    /// <summary>
    /// Füllt die Basis-Spielzeit aus der Spielzeit, die der Launcher lokal gespeichert hat.
    /// Gespeichert wird erst mit "Speichern", damit der Wert vorher prüfbar bleibt.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImportLauncherPlaytime))]
    private async Task ImportLauncherPlaytimeAsync()
    {
        if (PlaytimeReader is not { } reader || loadedGame?.ExternalGameId is not { } externalGameId)
        {
            return;
        }

        var launcher = LauncherName;
        int? launcherMinutes;
        try
        {
            launcherMinutes = await reader.GetPlaytimeMinutesAsync(externalGameId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"{launcher}-Spielzeit konnte nicht gelesen werden: {exception.Message}";
            return;
        }

        if (launcherMinutes is not > 0)
        {
            StatusMessage = $"{launcher} hat zu diesem Spiel keine Spielzeit gespeichert.";
            return;
        }

        // Der Launcher zählt auch die Zeit mit, die hier schon als Session erfasst ist.
        var tracked = TimeSpan.FromTicks(Sessions.Sum(session => session.Model.GetEffectiveDuration(clock.UtcNow).Ticks));
        var baseline = Math.Max(0, launcherMinutes.Value - (int)tracked.TotalMinutes);
        BaselinePlaytimeHours = baseline / 60;
        BaselinePlaytimeMinutes = baseline % 60;
        StatusMessage = $"{launcher} meldet {TimeFormatter.Format(TimeSpan.FromMinutes(launcherMinutes.Value))}, davon "
            + $"{TimeFormatter.Format(tracked)} hier bereits erfasst. Basis-Spielzeit: "
            + $"{TimeFormatter.Format(TimeSpan.FromMinutes(baseline))} – zum Übernehmen speichern.";
    }

    [RelayCommand(CanExecute = nameof(HasLoadedGame))]
    private async Task SaveGameAsync()
    {
        if (loadedGame is null)
        {
            return;
        }

        try
        {
            await catalog.UpdateGameAsync(
                loadedGame.Id,
                GameName,
                loadedGame.PrimaryExecutable?.ExecutablePath ?? string.Empty,
                DailyPlaytimeLimitMinutes > 0 ? (int)DailyPlaytimeLimitMinutes : null,
                WeeklyPlaytimeLimitMinutes > 0 ? (int)WeeklyPlaytimeLimitMinutes : null,
                GetBaselineMinutes(),
                TagsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                CancellationToken.None);
            await RefreshAsync();
            StatusMessage = "Spiel gespeichert";
        }
        catch (YFTimeTrackerException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Spiel konnte nicht gespeichert werden: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(SessionCanSave))]
    private async Task SaveSessionAsync()
    {
        if (!SessionCanSave || loadedGame is null)
        {
            StatusMessage = "Eine laufende Session kann nicht bearbeitet werden.";
            return;
        }

        if (EditorStartTime is not { } startTime || EditorEndTime is not { } endTime)
        {
            StatusMessage = "Bitte Start- und Endzeit vollständig angeben.";
            return;
        }

        if (!TryCreateUtc(EditorStartDate, startTime, out var startedAtUtc)
            || !TryCreateUtc(EditorEndDate, endTime, out var endedAtUtc))
        {
            StatusMessage = "Die gewählte Uhrzeit existiert wegen der Zeitumstellung nicht.";
            return;
        }

        try
        {
            var isNew = editingSessionId is null;
            if (editingSessionId is { } sessionId)
            {
                await sessionEditor.UpdateManualSessionAsync(sessionId, startedAtUtc, endedAtUtc, CancellationToken.None);
            }
            else
            {
                await sessionEditor.AddManualSessionAsync(loadedGame.Id, startedAtUtc, endedAtUtc, CancellationToken.None);
            }

            NewSession();
            await RefreshAsync();
            StatusMessage = isNew ? "Session hinzugefügt" : "Session gespeichert";
        }
        catch (YFTimeTrackerException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Session konnte nicht gespeichert werden: {exception.Message}";
        }
    }

    [RelayCommand]
    private void NewSession()
    {
        editingSessionId = null;
        selectedSession = null;
        OnPropertyChanged(nameof(SelectedSession));
        OnPropertyChanged(nameof(CanDeleteSelectedSession));
        SessionEditorEnabled = true;
        SessionEditorTitle = "NEUE SESSION";
        SessionEditorDescription = "Ergänze fehlende Spielzeit mit lokalen Start- und Endzeiten.";
        SessionSaveButtonText = "Session hinzufügen";
        ResetEditorTimes();
        StatusMessage = "Neue Session: Zeitraum auswählen.";
    }

    private void BeginEditSession(SessionListItemViewModel session)
    {
        editingSessionId = session.Id;
        SetEditorDateTimes(session.StartedAtUtc, session.EndedAtUtc ?? clock.UtcNow);
        SessionEditorEnabled = session.CanModify;
        SessionEditorTitle = session.IsOpen ? "LAUFENDE SESSION" : "SESSION BEARBEITEN";
        SessionEditorDescription = session.IsOpen
            ? "Die laufende Session wird automatisch verwaltet und ist schreibgeschützt."
            : "Passe Start und Ende an. Überschneidungen werden verhindert.";
        SessionSaveButtonText = "Änderungen speichern";
        StatusMessage = session.IsOpen ? "Laufende Session ausgewählt" : $"Session vom {session.StartedAt} ausgewählt";
    }

    private void ResetEditorTimes()
    {
        var endLocal = TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local);
        var startLocal = endLocal.AddHours(-1);
        SetEditorDateTimes(startLocal.ToUniversalTime(), endLocal.ToUniversalTime());
    }

    private void SetEditorDateTimes(DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc)
    {
        var startLocal = TimeZoneInfo.ConvertTime(startedAtUtc, TimeZoneInfo.Local);
        var endLocal = TimeZoneInfo.ConvertTime(endedAtUtc, TimeZoneInfo.Local);
        EditorStartDate = new DateTimeOffset(startLocal.Date, startLocal.Offset);
        EditorStartTime = startLocal.TimeOfDay;
        EditorEndDate = new DateTimeOffset(endLocal.Date, endLocal.Offset);
        EditorEndTime = endLocal.TimeOfDay;
    }

    private void ShowError(string message)
    {
        loadedGame = null;
        StatusMessage = message;
        ContentVisibility = Visibility.Collapsed;
        ErrorVisibility = Visibility.Visible;
        SaveGameCommand.NotifyCanExecuteChanged();
        SaveSessionCommand.NotifyCanExecuteChanged();
        LaunchGameCommand.NotifyCanExecuteChanged();
        ImportLauncherPlaytimeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLaunchGame))]
    private void LaunchGame()
    {
        if (gameLaunchService is null || loadedGame?.PrimaryExecutable is not { } executable)
        {
            return;
        }

        try
        {
            gameLaunchService.Launch(executable.ExecutablePath);
            StatusMessage = "Spiel wird gestartet …";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Spiel konnte nicht gestartet werden: {exception.Message}";
        }
    }

    private static string GetInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private static bool TryCreateUtc(DateTimeOffset date, TimeSpan time, out DateTimeOffset utc)
    {
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        utc = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
        return true;
    }
}

public sealed record GameExecutableItemViewModel(
    string Name,
    string Path,
    string Role,
    string Status,
    bool IsPrimary);

public sealed record GameDetailTimelinePointViewModel(
    string Label,
    string TooltipText,
    TimeSpan Duration,
    double BarHeight,
    string BarColor);
