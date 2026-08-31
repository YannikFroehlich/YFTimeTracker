using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.App.ViewModels;

public sealed class GameDetailsViewModel : ObservableObject
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly IGameRepository games;
    private readonly IGameCatalogService catalog;
    private readonly IGameSessionRepository sessionRepository;
    private readonly IGameSessionEditor sessionEditor;
    private readonly IClock clock;
    private readonly IGameIconService? gameIcons;
    private readonly AsyncRelayCommand saveGameCommand;
    private readonly AsyncRelayCommand saveSessionCommand;
    private Game? loadedGame;
    private long gameId;
    private SessionListItemViewModel? selectedSession;
    private long? editingSessionId;
    private string gameName = "Spiel";
    private string initials = "YF";
    private string? iconPath;
    private string sourceLabel = "MANUELL";
    private string sourceDetail = "Lokal hinzugefügt";
    private string installDirectory = "Kein Installationsordner hinterlegt";
    private string primaryExecutablePath = string.Empty;
    private string totalPlaytimeText = "0 min";
    private string sessionCountText = "Keine Sessions";
    private string averageSessionText = "0 min";
    private string lastPlayedText = "Noch nicht gespielt";
    private string chartMaximumText = "2 h";
    private string chartMiddleText = "1 h";
    private string statusMessage = "Spieldetails werden geladen …";
    private string sessionEditorTitle = "NEUE SESSION";
    private string sessionEditorDescription = "Ergänze fehlende Spielzeit mit lokalen Start- und Endzeiten.";
    private string sessionSaveButtonText = "Session hinzufügen";
    private DateTimeOffset editorStartDate;
    private DateTimeOffset editorEndDate;
    private TimeSpan? editorStartTime;
    private TimeSpan? editorEndTime;
    private bool sessionEditorEnabled = true;
    private Visibility contentVisibility = Visibility.Collapsed;
    private Visibility errorVisibility = Visibility.Collapsed;
    private Visibility sessionsVisibility = Visibility.Collapsed;
    private Visibility emptySessionsVisibility = Visibility.Visible;

    public GameDetailsViewModel(
        IGameRepository games,
        IGameCatalogService catalog,
        IGameSessionRepository sessionRepository,
        IGameSessionEditor sessionEditor,
        IClock clock,
        IGameIconService? gameIcons = null)
    {
        this.games = games;
        this.catalog = catalog;
        this.sessionRepository = sessionRepository;
        this.sessionEditor = sessionEditor;
        this.clock = clock;
        this.gameIcons = gameIcons;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        saveGameCommand = new AsyncRelayCommand(SaveGameAsync, () => loadedGame is not null);
        SaveGameCommand = saveGameCommand;
        NewSessionCommand = new RelayCommand(BeginNewSession);
        saveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => SessionCanSave);
        SaveSessionCommand = saveSessionCommand;
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

    public string Initials { get => initials; private set => SetProperty(ref initials, value); }

    public string? IconPath { get => iconPath; private set => SetProperty(ref iconPath, value); }

    public string SourceLabel { get => sourceLabel; private set => SetProperty(ref sourceLabel, value); }

    public string SourceDetail { get => sourceDetail; private set => SetProperty(ref sourceDetail, value); }

    public string InstallDirectory { get => installDirectory; private set => SetProperty(ref installDirectory, value); }

    public string PrimaryExecutablePath { get => primaryExecutablePath; private set => SetProperty(ref primaryExecutablePath, value); }

    public string TotalPlaytimeText { get => totalPlaytimeText; private set => SetProperty(ref totalPlaytimeText, value); }

    public string SessionCountText { get => sessionCountText; private set => SetProperty(ref sessionCountText, value); }

    public string AverageSessionText { get => averageSessionText; private set => SetProperty(ref averageSessionText, value); }

    public string LastPlayedText { get => lastPlayedText; private set => SetProperty(ref lastPlayedText, value); }

    public string ChartMaximumText { get => chartMaximumText; private set => SetProperty(ref chartMaximumText, value); }

    public string ChartMiddleText { get => chartMiddleText; private set => SetProperty(ref chartMiddleText, value); }

    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }

    public string SessionEditorTitle { get => sessionEditorTitle; private set => SetProperty(ref sessionEditorTitle, value); }

    public string SessionEditorDescription { get => sessionEditorDescription; private set => SetProperty(ref sessionEditorDescription, value); }

    public string SessionSaveButtonText { get => sessionSaveButtonText; private set => SetProperty(ref sessionSaveButtonText, value); }

    public DateTimeOffset EditorStartDate
    {
        get => editorStartDate;
        set => SetProperty(ref editorStartDate, value);
    }

    public DateTimeOffset EditorEndDate
    {
        get => editorEndDate;
        set => SetProperty(ref editorEndDate, value);
    }

    public TimeSpan? EditorStartTime
    {
        get => editorStartTime;
        set => SetProperty(ref editorStartTime, value);
    }

    public TimeSpan? EditorEndTime
    {
        get => editorEndTime;
        set => SetProperty(ref editorEndTime, value);
    }

    public bool SessionEditorEnabled
    {
        get => sessionEditorEnabled;
        private set
        {
            if (SetProperty(ref sessionEditorEnabled, value))
            {
                OnPropertyChanged(nameof(SessionCanSave));
                saveSessionCommand.NotifyCanExecuteChanged();
            }
        }
    }

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

    public Visibility ContentVisibility { get => contentVisibility; private set => SetProperty(ref contentVisibility, value); }

    public Visibility ErrorVisibility { get => errorVisibility; private set => SetProperty(ref errorVisibility, value); }

    public Visibility SessionsVisibility { get => sessionsVisibility; private set => SetProperty(ref sessionsVisibility, value); }

    public Visibility EmptySessionsVisibility { get => emptySessionsVisibility; private set => SetProperty(ref emptySessionsVisibility, value); }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand SaveGameCommand { get; }

    public IRelayCommand NewSessionCommand { get; }

    public IAsyncRelayCommand SaveSessionCommand { get; }

    public async Task LoadAsync(long requestedGameId)
    {
        gameId = requestedGameId;
        await RefreshAsync();
    }

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
                : await gameIcons.GetIconPathAsync(
                    game.PrimaryExecutable?.ExecutablePath,
                    CancellationToken.None);
            loadedGame = game;
            ApplyGame(game, resolvedIconPath);
            ApplySessions(storedSessions);
            ContentVisibility = Visibility.Visible;
            ErrorVisibility = Visibility.Collapsed;
            saveGameCommand.NotifyCanExecuteChanged();
            saveSessionCommand.NotifyCanExecuteChanged();
            StatusMessage = $"Zuletzt aktualisiert um {TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.Local):HH:mm}.";
        }
        catch (Exception exception)
        {
            ShowError($"Spieldetails konnten nicht geladen werden: {exception.Message}");
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
            BeginNewSession();
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
        SourceLabel = FormatSource(game.Source);
        SourceDetail = game.ExternalGameId is { Length: > 0 }
            ? $"Launcher-ID {game.ExternalGameId}"
            : game.Source == GameSource.Manual ? "Lokal hinzugefügt" : "Automatisch erkannt";
        InstallDirectory = string.IsNullOrWhiteSpace(game.InstallDirectory)
            ? "Kein Installationsordner hinterlegt"
            : game.InstallDirectory;
        PrimaryExecutablePath = game.PrimaryExecutable?.ExecutablePath ?? string.Empty;

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
        var total = TimeSpan.FromTicks(durations.Sum(duration => duration.Ticks));
        var average = durations.Length == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(total.Ticks / durations.Length);
        var lastPlayedAt = storedSessions
            .Select(session => session.EndedAtUtc ?? (session.IsOpen ? clock.UtcNow : session.LastSeenAtUtc))
            .DefaultIfEmpty()
            .Max();

        TotalPlaytimeText = TimeFormatter.Format(total);
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
                item.Date == today ? "#2CE5F3" : index % 3 == 2 ? "#8A4DFF" : "#387BFF"));
        }
    }

    private TimeSpan GetDurationForLocalDay(IEnumerable<GameSession> sessions, DateOnly date)
    {
        var startUtc = LocalDateStartToUtc(date);
        var endUtc = LocalDateStartToUtc(date.AddDays(1));
        var total = TimeSpan.Zero;
        foreach (var session in sessions)
        {
            var sessionEnd = session.EndedAtUtc ?? clock.UtcNow;
            var overlapStart = session.StartedAtUtc > startUtc ? session.StartedAtUtc : startUtc;
            var overlapEnd = sessionEnd < endUtc ? sessionEnd : endUtc;
            if (overlapEnd > overlapStart)
            {
                total += overlapEnd - overlapStart;
            }
        }

        return total;
    }

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
                CancellationToken.None);
            await RefreshAsync();
            StatusMessage = "Spielname gespeichert";
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

            BeginNewSession();
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

    private void BeginNewSession()
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
        saveGameCommand.NotifyCanExecuteChanged();
        saveSessionCommand.NotifyCanExecuteChanged();
    }

    private static string FormatSource(GameSource source) => source switch
    {
        GameSource.Steam => "STEAM",
        GameSource.Epic => "EPIC",
        GameSource.Gog => "GOG",
        GameSource.Xbox => "XBOX",
        GameSource.BattleNet => "BATTLE.NET",
        GameSource.Ubisoft => "UBISOFT",
        _ => "MANUELL"
    };

    private static string GetInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private static DateTimeOffset LocalDateStartToUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
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
