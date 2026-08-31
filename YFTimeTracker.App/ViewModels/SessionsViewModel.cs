using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.App.ViewModels;

public sealed class SessionsViewModel : ObservableObject
{
    private readonly IGameCatalogService catalog;
    private readonly IGameSessionRepository sessionRepository;
    private readonly IGameSessionEditor sessionEditor;
    private readonly IClock clock;
    private readonly IGameIconService? gameIcons;
    private readonly List<GameSession> loadedSessions = [];
    private readonly Dictionary<long, string?> iconPathsByGame = [];
    private readonly AsyncRelayCommand saveSessionCommand;
    private int refreshVersion;
    private SessionListItemViewModel? selectedSession;
    private SessionGameFilterOption? selectedGameFilter;
    private SessionPeriodOption selectedPeriod;
    private GameListItemViewModel? editorGame;
    private string searchText = string.Empty;
    private string statusMessage = "Bereit";
    private string sessionCountText = "0";
    private string totalDurationText = "0 min";
    private string averageDurationText = "0 min";
    private string emptyStateText = "Für diesen Filter wurden keine Sessions gefunden.";
    private DateTimeOffset editorStartDate;
    private DateTimeOffset editorEndDate;
    private TimeSpan? editorStartTime;
    private TimeSpan? editorEndTime;
    private long? editingSessionId;
    private bool editorFieldsEnabled = true;

    public SessionsViewModel(
        IGameCatalogService catalog,
        IGameSessionRepository sessionRepository,
        IGameSessionEditor sessionEditor,
        IClock clock,
        IGameIconService? gameIcons = null)
    {
        this.catalog = catalog;
        this.sessionRepository = sessionRepository;
        this.sessionEditor = sessionEditor;
        this.clock = clock;
        this.gameIcons = gameIcons;

        PeriodOptions =
        [
            new SessionPeriodOption(SessionPeriodKind.Today, "Heute"),
            new SessionPeriodOption(SessionPeriodKind.LastSevenDays, "Letzte 7 Tage"),
            new SessionPeriodOption(SessionPeriodKind.LastThirtyDays, "Letzte 30 Tage"),
            new SessionPeriodOption(SessionPeriodKind.All, "Gesamter Zeitraum")
        ];
        selectedPeriod = PeriodOptions[2];

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewSessionCommand = new RelayCommand(BeginNewSession);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        saveSessionCommand = new AsyncRelayCommand(SaveSessionAsync, () => EditorCanSave);
        SaveSessionCommand = saveSessionCommand;

        ResetEditorTimes();
    }

    public ObservableCollection<SessionListItemViewModel> Sessions { get; } = [];

    public ObservableCollection<SessionGameFilterOption> GameFilters { get; } = [];

    public ObservableCollection<GameListItemViewModel> EditorGames { get; } = [];

    public IReadOnlyList<SessionPeriodOption> PeriodOptions { get; }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value ?? string.Empty))
            {
                ApplyFilters();
            }
        }
    }

    public SessionGameFilterOption? SelectedGameFilter
    {
        get => selectedGameFilter;
        set
        {
            if (SetProperty(ref selectedGameFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public SessionPeriodOption SelectedPeriod
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

            OnPropertyChanged(nameof(CanDeleteSelected));
        }
    }

    public GameListItemViewModel? EditorGame
    {
        get => editorGame;
        set
        {
            if (SetProperty(ref editorGame, value))
            {
                NotifyEditorStateChanged();
            }
        }
    }

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

    public bool EditorFieldsEnabled
    {
        get => editorFieldsEnabled;
        private set
        {
            if (SetProperty(ref editorFieldsEnabled, value))
            {
                NotifyEditorStateChanged();
            }
        }
    }

    public bool EditorGameSelectionEnabled => EditorFieldsEnabled && editingSessionId is null;

    public bool EditorCanSave => EditorFieldsEnabled && EditorGame is not null;

    public bool CanDeleteSelected => SelectedSession?.CanModify == true;

    public string EditorTitle => editingSessionId is null ? "NEUE SESSION" : "SESSION BEARBEITEN";

    public string EditorDescription => SelectedSession?.IsOpen == true
        ? "Laufende Sessions werden vom Tracker verwaltet und können erst nach ihrem Ende geändert werden."
        : editingSessionId is null
            ? "Ergänze fehlende Spielzeit mit lokalen Start- und Endzeiten."
            : "Passe Start und Ende an. Überschneidungen mit anderen Sessions werden verhindert.";

    public string SaveButtonText => editingSessionId is null ? "Session hinzufügen" : "Änderungen speichern";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string SessionCountText
    {
        get => sessionCountText;
        private set => SetProperty(ref sessionCountText, value);
    }

    public string TotalDurationText
    {
        get => totalDurationText;
        private set => SetProperty(ref totalDurationText, value);
    }

    public string AverageDurationText
    {
        get => averageDurationText;
        private set => SetProperty(ref averageDurationText, value);
    }

    public string EmptyStateText
    {
        get => emptyStateText;
        private set => SetProperty(ref emptyStateText, value);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand NewSessionCommand { get; }

    public IRelayCommand ClearFiltersCommand { get; }

    public IAsyncRelayCommand SaveSessionCommand { get; }

    public async Task RefreshAsync()
    {
        var currentRefresh = Interlocked.Increment(ref refreshVersion);
        try
        {
            var selectedId = SelectedSession?.Id;
            var selectedFilterGameId = SelectedGameFilter?.GameId;
            var selectedEditorGameId = EditorGame?.Id;
            var selectedPeriodSnapshot = SelectedPeriod;
            var (fromUtc, toUtc) = GetPeriodRangeUtc(selectedPeriodSnapshot.Kind);
            var games = await catalog.GetGamesAsync(CancellationToken.None);
            var storedSessions = await sessionRepository.GetSessionsAsync(fromUtc, toUtc, CancellationToken.None);
            var resolvedIconPaths = await ResolveIconPathsAsync(games);
            if (currentRefresh != Volatile.Read(ref refreshVersion))
            {
                return;
            }

            GameFilters.Clear();
            GameFilters.Add(new SessionGameFilterOption(null, "Alle Spiele"));
            foreach (var game in games)
            {
                GameFilters.Add(new SessionGameFilterOption(game.Id, game.Name));
            }

            selectedGameFilter = GameFilters.FirstOrDefault(option => option.GameId == selectedFilterGameId)
                ?? GameFilters.First();
            OnPropertyChanged(nameof(SelectedGameFilter));

            EditorGames.Clear();
            iconPathsByGame.Clear();
            foreach (var game in games)
            {
                var iconPath = resolvedIconPaths.GetValueOrDefault(game.Id);
                iconPathsByGame[game.Id] = iconPath;
                EditorGames.Add(new GameListItemViewModel(game, iconPath));
            }

            editorGame = EditorGames.FirstOrDefault(game => game.Id == selectedEditorGameId)
                ?? EditorGames.FirstOrDefault();
            OnPropertyChanged(nameof(EditorGame));
            NotifyEditorStateChanged();

            loadedSessions.Clear();
            loadedSessions.AddRange(storedSessions);
            ApplyFilters(selectedId);

            StatusMessage = $"{Sessions.Count} Session(s) · {selectedPeriodSnapshot.Label}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Sessions konnten nicht geladen werden: {exception.Message}";
        }
    }

    public async Task ShowSessionAsync(long sessionId)
    {
        searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        selectedGameFilter = null;
        OnPropertyChanged(nameof(SelectedGameFilter));
        selectedPeriod = PeriodOptions.Single(option => option.Kind == SessionPeriodKind.All);
        OnPropertyChanged(nameof(SelectedPeriod));
        await RefreshAsync();

        SelectedSession = Sessions.FirstOrDefault(session => session.Id == sessionId);
        if (SelectedSession is null)
        {
            StatusMessage = "Die ausgewählte Session wurde nicht gefunden.";
        }
    }

    public void RefreshLiveDurations()
    {
        var now = clock.UtcNow;
        foreach (var session in Sessions)
        {
            session.RefreshDuration(now);
        }

        UpdateSummary();
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedSession is null)
        {
            StatusMessage = "Bitte zuerst eine Session auswählen.";
            return;
        }

        try
        {
            await sessionEditor.DeleteSessionAsync(SelectedSession.Id, CancellationToken.None);
            SelectedSession = null;
            editingSessionId = null;
            await RefreshAsync();
            BeginNewSession(updateStatus: false);
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

    private async Task SaveSessionAsync()
    {
        if (!EditorCanSave || EditorGame is null)
        {
            StatusMessage = SelectedSession?.IsOpen == true
                ? "Eine laufende Session kann nicht bearbeitet werden."
                : "Bitte zuerst ein Spiel auswählen.";
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
            var isNewSession = editingSessionId is null;
            long savedSessionId;
            if (editingSessionId is { } sessionId)
            {
                await sessionEditor.UpdateManualSessionAsync(
                    sessionId,
                    startedAtUtc,
                    endedAtUtc,
                    CancellationToken.None);
                savedSessionId = sessionId;
            }
            else
            {
                var added = await sessionEditor.AddManualSessionAsync(
                    EditorGame.Id,
                    startedAtUtc,
                    endedAtUtc,
                    CancellationToken.None);
                savedSessionId = added.Id;
            }

            await RefreshAsync();
            SelectedSession = Sessions.FirstOrDefault(session => session.Id == savedSessionId);
            StatusMessage = isNewSession ? "Session hinzugefügt" : "Session gespeichert";
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
        BeginNewSession(updateStatus: true);
    }

    private void BeginNewSession(bool updateStatus)
    {
        selectedSession = null;
        OnPropertyChanged(nameof(SelectedSession));
        editingSessionId = null;
        EditorFieldsEnabled = true;
        EditorGame = EditorGames.FirstOrDefault();
        ResetEditorTimes();
        NotifyEditorStateChanged();
        OnPropertyChanged(nameof(CanDeleteSelected));
        if (updateStatus)
        {
            StatusMessage = EditorGames.Count == 0
                ? "Lege zuerst ein Spiel in der Bibliothek an."
                : "Neue Session: Spiel und Zeitraum auswählen.";
        }
    }

    private void BeginEditSession(SessionListItemViewModel session)
    {
        editingSessionId = session.Id;
        editorGame = EditorGames.FirstOrDefault(game => game.Id == session.GameId);
        OnPropertyChanged(nameof(EditorGame));

        SetEditorDateTimes(session.StartedAtUtc, session.EndedAtUtc ?? clock.UtcNow);
        EditorFieldsEnabled = session.CanModify;
        NotifyEditorStateChanged();
        StatusMessage = session.IsOpen
            ? "Diese Session läuft aktuell und ist schreibgeschützt."
            : $"Session von {session.GameName} ausgewählt";
    }

    private void ClearFilters()
    {
        searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
        selectedGameFilter = GameFilters.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedGameFilter));
        SelectedPeriod = PeriodOptions[2];
        ApplyFilters();
    }

    private void ApplyFilters(long? selectedId = null)
    {
        selectedId ??= SelectedSession?.Id;
        var search = SearchText.Trim();
        var gameId = SelectedGameFilter?.GameId;
        var filtered = loadedSessions
            .Where(session => gameId is null || session.GameId == gameId)
            .Where(session => string.IsNullOrWhiteSpace(search)
                || (session.Game?.Name?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
                || session.StartedAtUtc.LocalDateTime.ToString("g").Contains(search, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(session => session.StartedAtUtc)
            .Select(session => new SessionListItemViewModel(
                session,
                clock.UtcNow,
                iconPathsByGame.GetValueOrDefault(session.GameId)))
            .ToArray();

        Sessions.Clear();
        foreach (var session in filtered)
        {
            Sessions.Add(session);
        }

        var matchingSelection = selectedId is null
            ? null
            : Sessions.FirstOrDefault(session => session.Id == selectedId.Value);
        selectedSession = matchingSelection;
        OnPropertyChanged(nameof(SelectedSession));
        OnPropertyChanged(nameof(CanDeleteSelected));
        if (selectedId is not null && matchingSelection is null)
        {
            editingSessionId = null;
            EditorFieldsEnabled = true;
            editorGame = EditorGames.FirstOrDefault();
            OnPropertyChanged(nameof(EditorGame));
            ResetEditorTimes();
            NotifyEditorStateChanged();
        }

        EmptyStateText = loadedSessions.Count == 0
            ? "In diesem Zeitraum wurden noch keine Sessions aufgezeichnet."
            : "Für die aktuellen Such- und Spielfilter wurden keine Sessions gefunden.";
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var durations = Sessions.Select(session => session.EffectiveDuration).ToArray();
        var total = TimeSpan.FromSeconds(durations.Sum(duration => duration.TotalSeconds));
        var average = durations.Length == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(total.TotalSeconds / durations.Length);

        SessionCountText = Sessions.Count.ToString("N0");
        TotalDurationText = TimeFormatter.Format(total);
        AverageDurationText = TimeFormatter.Format(average);
    }

    private async Task<IReadOnlyDictionary<long, string?>> ResolveIconPathsAsync(IReadOnlyList<Game> games)
    {
        if (gameIcons is null || games.Count == 0)
        {
            return new Dictionary<long, string?>();
        }

        var tasks = games.Select(async game => new
        {
            game.Id,
            IconPath = await gameIcons.GetIconPathAsync(
                game.PrimaryExecutable?.ExecutablePath,
                CancellationToken.None)
        });
        var resolved = await Task.WhenAll(tasks);
        return resolved.ToDictionary(item => item.Id, item => item.IconPath);
    }

    private (DateTimeOffset? FromUtc, DateTimeOffset? ToUtc) GetPeriodRangeUtc(SessionPeriodKind period)
    {
        if (period == SessionPeriodKind.All)
        {
            return (null, null);
        }

        var localToday = clock.UtcNow.ToLocalTime().Date;
        var startDate = period switch
        {
            SessionPeriodKind.LastSevenDays => localToday.AddDays(-6),
            SessionPeriodKind.LastThirtyDays => localToday.AddDays(-29),
            _ => localToday
        };
        var endDate = localToday.AddDays(1);
        return (ConvertLocalDateToUtc(startDate), ConvertLocalDateToUtc(endDate));
    }

    private void ResetEditorTimes()
    {
        var endLocal = clock.UtcNow.ToLocalTime();
        var startLocal = endLocal.AddHours(-1);
        SetEditorDateTimes(startLocal.ToUniversalTime(), endLocal.ToUniversalTime());
    }

    private void SetEditorDateTimes(DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc)
    {
        var startLocal = startedAtUtc.ToLocalTime();
        var endLocal = endedAtUtc.ToLocalTime();
        EditorStartDate = new DateTimeOffset(startLocal.Date, startLocal.Offset);
        EditorStartTime = startLocal.TimeOfDay;
        EditorEndDate = new DateTimeOffset(endLocal.Date, endLocal.Offset);
        EditorEndTime = endLocal.TimeOfDay;
    }

    private void NotifyEditorStateChanged()
    {
        OnPropertyChanged(nameof(EditorGameSelectionEnabled));
        OnPropertyChanged(nameof(EditorCanSave));
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(EditorDescription));
        OnPropertyChanged(nameof(SaveButtonText));
        saveSessionCommand.NotifyCanExecuteChanged();
    }

    private static DateTimeOffset ConvertLocalDateToUtc(DateTime localDate)
    {
        var unspecified = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
    }

    private static bool TryCreateUtc(DateTimeOffset date, TimeSpan time, out DateTimeOffset utc)
    {
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        var offset = TimeZoneInfo.Local.GetUtcOffset(local);
        utc = new DateTimeOffset(local, offset).ToUniversalTime();
        return true;
    }
}

public sealed record SessionGameFilterOption(long? GameId, string Name);

public sealed record SessionPeriodOption(SessionPeriodKind Kind, string Label);

public enum SessionPeriodKind
{
    Today,
    LastSevenDays,
    LastThirtyDays,
    All
}
