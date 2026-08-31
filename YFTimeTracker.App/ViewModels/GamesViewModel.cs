using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.App.ViewModels;

public sealed class GamesViewModel : ObservableObject
{
    private readonly IGameCatalogService catalog;
    private readonly IGameSessionRepository sessions;
    private readonly IGameSessionEditor sessionEditor;
    private readonly IFilePickerService filePicker;
    private readonly IGameTrackingService trackingService;
    private readonly IClock clock;
    private readonly List<GameListItemViewModel> allGames = [];
    private GameListItemViewModel? selectedGame;
    private SessionListItemViewModel? selectedSession;
    private LibrarySourceFilterOption selectedSourceFilter;
    private LibraryStatusFilterOption selectedStatusFilter;
    private LibrarySortOption selectedSortOption;
    private string searchText = string.Empty;
    private string resultSummary = "0 Spiele";
    private string emptyStateText = "Noch keine Spiele in der Bibliothek.";
    private Visibility gamesVisibility = Visibility.Collapsed;
    private Visibility emptyVisibility = Visibility.Visible;
    private string displayName = string.Empty;
    private string executablePath = string.Empty;
    private string manualStartText = DateTime.Now.AddHours(-1).ToString("g", CultureInfo.CurrentCulture);
    private string manualEndText = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
    private string statusMessage = "Bereit";

    public GamesViewModel(
        IGameCatalogService catalog,
        IGameSessionRepository sessions,
        IGameSessionEditor sessionEditor,
        IFilePickerService filePicker,
        IGameTrackingService trackingService,
        IClock clock)
    {
        this.catalog = catalog;
        this.sessions = sessions;
        this.sessionEditor = sessionEditor;
        this.filePicker = filePicker;
        this.trackingService = trackingService;
        this.clock = clock;

        SourceFilters =
        [
            new LibrarySourceFilterOption(null, "Alle Quellen"),
            new LibrarySourceFilterOption(GameSource.Steam, "Steam"),
            new LibrarySourceFilterOption(GameSource.Epic, "Epic"),
            new LibrarySourceFilterOption(GameSource.Gog, "GOG"),
            new LibrarySourceFilterOption(GameSource.Xbox, "Xbox"),
            new LibrarySourceFilterOption(GameSource.BattleNet, "Battle.net"),
            new LibrarySourceFilterOption(GameSource.Ubisoft, "Ubisoft Connect"),
            new LibrarySourceFilterOption(GameSource.Manual, "Manuell")
        ];
        selectedSourceFilter = SourceFilters[0];
        StatusFilters =
        [
            new LibraryStatusFilterOption(LibraryStatusFilterKind.All, "Alle Status"),
            new LibraryStatusFilterOption(LibraryStatusFilterKind.Running, "Aktuell aktiv"),
            new LibraryStatusFilterOption(LibraryStatusFilterKind.MissingExecutable, "EXE fehlt")
        ];
        selectedStatusFilter = StatusFilters[0];
        SortOptions =
        [
            new LibrarySortOption(LibrarySortKind.LastPlayed, "Zuletzt gespielt"),
            new LibrarySortOption(LibrarySortKind.Playtime, "Meiste Spielzeit"),
            new LibrarySortOption(LibrarySortKind.Name, "Name A–Z")
        ];
        selectedSortOption = SortOptions[0];

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        NewGameCommand = new RelayCommand(NewGame);
        BrowseExecutableCommand = new AsyncRelayCommand(BrowseExecutableAsync);
        AddOrUpdateGameCommand = new AsyncRelayCommand(AddOrUpdateGameAsync);
        DeleteSelectedGameCommand = new AsyncRelayCommand(DeleteSelectedGameAsync);
        AddManualSessionCommand = new AsyncRelayCommand(AddManualSessionAsync);
        DeleteSelectedSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync);
    }

    public ObservableCollection<GameListItemViewModel> Games { get; } = [];

    public ObservableCollection<SessionListItemViewModel> Sessions { get; } = [];

    public IReadOnlyList<LibrarySourceFilterOption> SourceFilters { get; }

    public IReadOnlyList<LibraryStatusFilterOption> StatusFilters { get; }

    public IReadOnlyList<LibrarySortOption> SortOptions { get; }

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

    public LibrarySourceFilterOption SelectedSourceFilter
    {
        get => selectedSourceFilter;
        set
        {
            if (value is not null && SetProperty(ref selectedSourceFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public LibraryStatusFilterOption SelectedStatusFilter
    {
        get => selectedStatusFilter;
        set
        {
            if (value is not null && SetProperty(ref selectedStatusFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    public LibrarySortOption SelectedSortOption
    {
        get => selectedSortOption;
        set
        {
            if (value is not null && SetProperty(ref selectedSortOption, value))
            {
                ApplyFilters();
            }
        }
    }

    public string ResultSummary
    {
        get => resultSummary;
        private set => SetProperty(ref resultSummary, value);
    }

    public string EmptyStateText
    {
        get => emptyStateText;
        private set => SetProperty(ref emptyStateText, value);
    }

    public Visibility GamesVisibility
    {
        get => gamesVisibility;
        private set => SetProperty(ref gamesVisibility, value);
    }

    public Visibility EmptyVisibility
    {
        get => emptyVisibility;
        private set => SetProperty(ref emptyVisibility, value);
    }

    public GameListItemViewModel? SelectedGame
    {
        get => selectedGame;
        set
        {
            if (!SetProperty(ref selectedGame, value))
            {
                return;
            }

            if (value is not null)
            {
                DisplayName = value.Name;
                ExecutablePath = value.ExecutablePath;
                _ = LoadSessionsAsync();
                return;
            }

            SelectedSession = null;
            DisplayName = string.Empty;
            ExecutablePath = string.Empty;
            Sessions.Clear();
        }
    }

    public SessionListItemViewModel? SelectedSession
    {
        get => selectedSession;
        set => SetProperty(ref selectedSession, value);
    }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string ExecutablePath
    {
        get => executablePath;
        set => SetProperty(ref executablePath, value);
    }

    public string ManualStartText
    {
        get => manualStartText;
        set => SetProperty(ref manualStartText, value);
    }

    public string ManualEndText
    {
        get => manualEndText;
        set => SetProperty(ref manualEndText, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand ClearFiltersCommand { get; }

    public IRelayCommand NewGameCommand { get; }

    public IAsyncRelayCommand BrowseExecutableCommand { get; }

    public IAsyncRelayCommand AddOrUpdateGameCommand { get; }

    public IAsyncRelayCommand DeleteSelectedGameCommand { get; }

    public IAsyncRelayCommand AddManualSessionCommand { get; }

    public IAsyncRelayCommand DeleteSelectedSessionCommand { get; }

    public async Task RefreshAsync()
    {
        try
        {
            var storedGames = await catalog.GetGamesAsync(CancellationToken.None);
            var storedSessions = await sessions.GetSessionsAsync(null, null, CancellationToken.None);
            var sessionsByGame = storedSessions
                .GroupBy(session => session.GameId)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<GameSession>)group.ToArray());
            var runningGameIds = trackingService.State.RunningGames
                .Select(game => game.GameId)
                .ToHashSet();

            allGames.Clear();
            foreach (var game in storedGames)
            {
                sessionsByGame.TryGetValue(game.Id, out var gameSessions);
                allGames.Add(new GameListItemViewModel(
                    game,
                    gameSessions,
                    runningGameIds.Contains(game.Id),
                    clock.UtcNow));
            }

            ApplyFilters();
            StatusMessage = allGames.Count == 0
                ? "Noch keine Spiele registriert"
                : $"{allGames.Count} {(allGames.Count == 1 ? "Spiel" : "Spiele")} registriert";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Bibliothek konnte nicht geladen werden: {exception.Message}";
        }
    }

    private void ApplyFilters()
    {
        var selectedId = SelectedGame?.Id;
        var query = allGames.AsEnumerable();
        var search = SearchText.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(game => game.SearchableText.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        if (SelectedSourceFilter.Source is { } source)
        {
            query = query.Where(game => game.Source == source);
        }

        query = SelectedStatusFilter.Kind switch
        {
            LibraryStatusFilterKind.Running => query.Where(game => game.IsRunning),
            LibraryStatusFilterKind.MissingExecutable => query.Where(game => !game.Exists),
            _ => query
        };

        query = SelectedSortOption.Kind switch
        {
            LibrarySortKind.Playtime => query
                .OrderByDescending(game => game.IsRunning)
                .ThenByDescending(game => game.TotalDuration)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),
            LibrarySortKind.Name => query.OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query
                .OrderByDescending(game => game.IsRunning)
                .ThenByDescending(game => game.LastPlayedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        var filtered = query.ToArray();
        Games.Clear();
        foreach (var game in filtered)
        {
            Games.Add(game);
        }

        var matchingSelection = selectedId is null
            ? null
            : Games.FirstOrDefault(game => game.Id == selectedId.Value);
        if (!ReferenceEquals(SelectedGame, matchingSelection))
        {
            SelectedGame = matchingSelection;
        }

        if (SelectedGame is null)
        {
            Sessions.Clear();
        }

        ResultSummary = allGames.Count == 0
            ? "Keine Spiele"
            : Games.Count == allGames.Count
                ? $"{allGames.Count} {(allGames.Count == 1 ? "Spiel" : "Spiele")}"
                : $"{Games.Count} von {allGames.Count} Spielen";
        EmptyStateText = allGames.Count == 0
            ? "Noch keine Spiele vorhanden. Starte ein Launcher-Spiel oder füge eines manuell hinzu."
            : "Für die aktuellen Such- und Filtereinstellungen wurden keine Spiele gefunden.";
        GamesVisibility = Games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyVisibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearFilters()
    {
        searchText = string.Empty;
        selectedSourceFilter = SourceFilters[0];
        selectedStatusFilter = StatusFilters[0];
        selectedSortOption = SortOptions[0];
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedSourceFilter));
        OnPropertyChanged(nameof(SelectedStatusFilter));
        OnPropertyChanged(nameof(SelectedSortOption));
        ApplyFilters();
        StatusMessage = "Bibliotheksfilter zurückgesetzt";
    }

    private void NewGame()
    {
        SelectedGame = null;
        SelectedSession = null;
        DisplayName = string.Empty;
        ExecutablePath = string.Empty;
        Sessions.Clear();
        StatusMessage = "Neues Spiel: Name und EXE-Pfad eingeben.";
    }

    private async Task BrowseExecutableAsync()
    {
        var path = await filePicker.PickExecutableAsync(CancellationToken.None);
        if (path is null)
        {
            return;
        }

        ExecutablePath = path;
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            DisplayName = Path.GetFileNameWithoutExtension(path);
        }
    }

    private async Task AddOrUpdateGameAsync()
    {
        try
        {
            if (SelectedGame is null)
            {
                await catalog.AddGameAsync(ExecutablePath, DisplayName, CancellationToken.None);
                DisplayName = string.Empty;
                ExecutablePath = string.Empty;
            }
            else
            {
                await catalog.UpdateGameAsync(SelectedGame.Id, DisplayName, ExecutablePath, CancellationToken.None);
            }

            await RefreshAsync();
            StatusMessage = "Spiel gespeichert";
        }
        catch (YFTimeTrackerException ex)
        {
            StatusMessage = ex.Message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Speichern fehlgeschlagen: {ex.Message}";
        }
    }

    private async Task DeleteSelectedGameAsync()
    {
        if (SelectedGame is null)
        {
            return;
        }

        await catalog.DeleteGameAsync(SelectedGame.Id, CancellationToken.None);
        SelectedGame = null;
        DisplayName = string.Empty;
        ExecutablePath = string.Empty;
        await RefreshAsync();
        StatusMessage = "Spiel gelöscht";
    }

    private async Task AddManualSessionAsync()
    {
        if (SelectedGame is null)
        {
            StatusMessage = "Bitte zuerst ein Spiel auswählen.";
            return;
        }

        if (!TryParseLocalDateTime(ManualStartText, out var startedAtUtc) ||
            !TryParseLocalDateTime(ManualEndText, out var endedAtUtc))
        {
            StatusMessage = "Bitte Start und Ende als lokales Datum/Uhrzeit eingeben.";
            return;
        }

        try
        {
            await sessionEditor.AddManualSessionAsync(SelectedGame.Id, startedAtUtc, endedAtUtc, CancellationToken.None);
            await LoadSessionsAsync();
            StatusMessage = "Session hinzugefügt";
        }
        catch (YFTimeTrackerException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        try
        {
            await sessionEditor.DeleteSessionAsync(SelectedSession.Id, CancellationToken.None);
            await LoadSessionsAsync();
            StatusMessage = "Session gelöscht";
        }
        catch (YFTimeTrackerException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private async Task LoadSessionsAsync()
    {
        if (SelectedGame is not { } game)
        {
            Sessions.Clear();
            return;
        }

        var gameId = game.Id;
        var storedSessions = await sessions.GetSessionsForGameAsync(gameId, CancellationToken.None);
        if (SelectedGame?.Id != gameId)
        {
            return;
        }

        Sessions.Clear();
        foreach (var session in storedSessions)
        {
            Sessions.Add(new SessionListItemViewModel(session));
        }
    }

    private static bool TryParseLocalDateTime(string text, out DateTimeOffset utc)
    {
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var local))
        {
            utc = new DateTimeOffset(local).ToUniversalTime();
            return true;
        }

        utc = default;
        return false;
    }
}

public sealed record LibrarySourceFilterOption(GameSource? Source, string Label);

public sealed record LibraryStatusFilterOption(LibraryStatusFilterKind Kind, string Label);

public sealed record LibrarySortOption(LibrarySortKind Kind, string Label);

public enum LibraryStatusFilterKind
{
    All,
    Running,
    MissingExecutable
}

public enum LibrarySortKind
{
    LastPlayed,
    Playtime,
    Name
}
