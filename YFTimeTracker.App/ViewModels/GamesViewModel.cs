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

public sealed partial class GamesViewModel : ObservableObject
{
    private readonly IGameCatalogService catalog;
    private readonly IGameSessionRepository sessions;
    private readonly IGameSessionEditor sessionEditor;
    private readonly IFilePickerService filePicker;
    private readonly IGameTrackingService trackingService;
    private readonly IClock clock;
    private readonly IGameIconService? gameIcons;
    private readonly List<GameListItemViewModel> allGames = [];
    private GameListItemViewModel? selectedGame;
    private LibrarySourceFilterOption selectedSourceFilter;
    private LibraryStatusFilterOption selectedStatusFilter;
    private LibrarySortOption selectedSortOption;
    private LibraryTagFilterOption selectedTagFilter;
    private string searchText = string.Empty;
    private bool isSyncingGames;

    public GamesViewModel(
        IGameCatalogService catalog,
        IGameSessionRepository sessions,
        IGameSessionEditor sessionEditor,
        IFilePickerService filePicker,
        IGameTrackingService trackingService,
        IClock clock,
        IGameIconService? gameIcons = null)
    {
        this.catalog = catalog;
        this.sessions = sessions;
        this.sessionEditor = sessionEditor;
        this.filePicker = filePicker;
        this.trackingService = trackingService;
        this.clock = clock;
        this.gameIcons = gameIcons;

        SourceFilters =
        [
            new LibrarySourceFilterOption(null, "Alle Quellen"),
            new LibrarySourceFilterOption(GameSource.Steam, "Steam"),
            new LibrarySourceFilterOption(GameSource.Epic, "Epic"),
            new LibrarySourceFilterOption(GameSource.Gog, "GOG"),
            new LibrarySourceFilterOption(GameSource.Xbox, "Xbox"),
            new LibrarySourceFilterOption(GameSource.BattleNet, "Battle.net"),
            new LibrarySourceFilterOption(GameSource.Ubisoft, "Ubisoft Connect"),
            new LibrarySourceFilterOption(GameSource.EaApp, "EA app"),
            new LibrarySourceFilterOption(GameSource.Riot, "Riot Client"),
            new LibrarySourceFilterOption(GameSource.Rockstar, "Rockstar"),
            new LibrarySourceFilterOption(GameSource.Amazon, "Amazon Games"),
            new LibrarySourceFilterOption(GameSource.Itch, "itch.io"),
            new LibrarySourceFilterOption(GameSource.Humble, "Humble App"),
            new LibrarySourceFilterOption(GameSource.Manual, "Manuell")
        ];
        selectedSourceFilter = SourceFilters[0];
        StatusFilters =
        [
            new LibraryStatusFilterOption(LibraryStatusFilterKind.All, "Alle Status"),
            new LibraryStatusFilterOption(LibraryStatusFilterKind.Pinned, "Nur angeheftete"),
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
        selectedTagFilter = TagFilters[0];

    }

    public ObservableCollection<GameListItemViewModel> Games { get; } = [];

    public ObservableCollection<SessionListItemViewModel> Sessions { get; } = [];

    public IReadOnlyList<LibrarySourceFilterOption> SourceFilters { get; }

    public IReadOnlyList<LibraryStatusFilterOption> StatusFilters { get; }

    public IReadOnlyList<LibrarySortOption> SortOptions { get; }

    public ObservableCollection<LibraryTagFilterOption> TagFilters { get; } = [new LibraryTagFilterOption(null, "Alle Tags")];

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

    public LibraryTagFilterOption SelectedTagFilter
    {
        get => selectedTagFilter;
        set
        {
            if (value is not null && SetProperty(ref selectedTagFilter, value))
            {
                ApplyFilters();
            }
        }
    }

    [ObservableProperty]
    public partial string ResultSummary { get; private set; } = "0 Spiele";

    [ObservableProperty]
    public partial string EmptyStateText { get; private set; } = "Noch keine Spiele in der Bibliothek.";

    [ObservableProperty]
    public partial Visibility GamesVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility EmptyVisibility { get; private set; } = Visibility.Visible;

    public GameListItemViewModel? SelectedGame
    {
        get => selectedGame;
        set
        {
            // Beim Abgleich der Liste meldet die ListView kurz null, wenn sie den ausgewählten
            // Eintrag verschiebt. Das ist keine Auswahl des Nutzers und darf die Eingaben nicht leeren.
            if (isSyncingGames || !SetProperty(ref selectedGame, value))
            {
                return;
            }

            RefreshMergeTargets();

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

    public ObservableCollection<GameListItemViewModel> MergeTargets { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMergeSelectedGame))]
    public partial GameListItemViewModel? SelectedMergeTarget { get; set; }

    public bool CanMergeSelectedGame => SelectedGame is not null && SelectedMergeTarget is not null;

    [ObservableProperty]
    public partial SessionListItemViewModel? SelectedSession { get; set; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ExecutablePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ManualStartText { get; set; } = DateTime.Now.AddHours(-1).ToString("g", CultureInfo.CurrentCulture);

    [ObservableProperty]
    public partial string ManualEndText { get; set; } = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "Bereit";

    [RelayCommand]
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
            var iconPaths = await ResolveIconPathsAsync(storedGames);

            // Ungespeicherte Eingaben im Editor überleben den Refresh. Hat der Nutzer nichts
            // geändert, folgt der Editor dem neu geladenen Stand (etwa einer Umbenennung).
            var editorUnchanged = SelectedGame is { } selected
                && DisplayName == selected.Name
                && ExecutablePath == selected.ExecutablePath;

            var existingById = allGames.ToDictionary(item => item.Id);
            allGames.Clear();
            foreach (var game in storedGames)
            {
                sessionsByGame.TryGetValue(game.Id, out var gameSessions);
                var isRunning = runningGameIds.Contains(game.Id);
                var iconPath = iconPaths.GetValueOrDefault(game.Id);
                if (existingById.TryGetValue(game.Id, out var item))
                {
                    item.Update(game, gameSessions, isRunning, clock.UtcNow, iconPath);
                }
                else
                {
                    item = new GameListItemViewModel(game, gameSessions, isRunning, clock.UtcNow, iconPath);
                }

                allGames.Add(item);
            }

            UpdateTagFilters();
            ApplyFilters();
            if (editorUnchanged && SelectedGame is { } refreshed)
            {
                DisplayName = refreshed.Name;
                ExecutablePath = refreshed.ExecutablePath;
            }

            await LoadSessionsAsync();
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
            LibraryStatusFilterKind.Pinned => query.Where(game => game.IsPinned),
            LibraryStatusFilterKind.Running => query.Where(game => game.IsRunning),
            LibraryStatusFilterKind.MissingExecutable => query.Where(game => !game.Exists),
            _ => query
        };

        if (SelectedTagFilter.Tag is { } tag)
        {
            query = query.Where(game => game.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }

        query = SelectedSortOption.Kind switch
        {
            LibrarySortKind.Playtime => query
                .OrderByDescending(game => game.IsPinned)
                .ThenByDescending(game => game.IsRunning)
                .ThenByDescending(game => game.TotalDuration)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),
            LibrarySortKind.Name => query
                .OrderByDescending(game => game.IsPinned)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => query
                .OrderByDescending(game => game.IsPinned)
                .ThenByDescending(game => game.IsRunning)
                .ThenByDescending(game => game.LastPlayedAtUtc ?? DateTimeOffset.MinValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        var filtered = query.ToArray();
        isSyncingGames = true;
        try
        {
            Games.SyncWith(filtered);
        }
        finally
        {
            isSyncingGames = false;
        }

        var matchingSelection = selectedId is null
            ? null
            : Games.FirstOrDefault(game => game.Id == selectedId.Value);
        if (!ReferenceEquals(SelectedGame, matchingSelection))
        {
            SelectedGame = matchingSelection;
        }
        else
        {
            // Stellt die Auswahl der ListView wieder her, falls sie beim Abgleich verloren ging.
            OnPropertyChanged(nameof(SelectedGame));
        }

        if (SelectedGame is null)
        {
            Sessions.Clear();
        }

        RefreshMergeTargets();

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

    [RelayCommand]
    private void ClearFilters()
    {
        searchText = string.Empty;
        selectedSourceFilter = SourceFilters[0];
        selectedStatusFilter = StatusFilters[0];
        selectedSortOption = SortOptions[0];
        selectedTagFilter = TagFilters[0];
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(SelectedSourceFilter));
        OnPropertyChanged(nameof(SelectedStatusFilter));
        OnPropertyChanged(nameof(SelectedSortOption));
        OnPropertyChanged(nameof(SelectedTagFilter));
        ApplyFilters();
        StatusMessage = "Bibliotheksfilter zurückgesetzt";
    }

    private void UpdateTagFilters()
    {
        var previousTag = SelectedTagFilter.Tag;
        var tags = allGames
            .SelectMany(game => game.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase);

        TagFilters.Clear();
        TagFilters.Add(new LibraryTagFilterOption(null, "Alle Tags"));
        foreach (var tag in tags)
        {
            TagFilters.Add(new LibraryTagFilterOption(tag, tag));
        }

        selectedTagFilter = TagFilters.FirstOrDefault(option => string.Equals(option.Tag, previousTag, StringComparison.OrdinalIgnoreCase))
            ?? TagFilters[0];
        OnPropertyChanged(nameof(SelectedTagFilter));
    }

    [RelayCommand]
    private async Task TogglePinAsync(long gameId)
    {
        var item = allGames.FirstOrDefault(game => game.Id == gameId);
        if (item is null)
        {
            return;
        }

        var newState = !item.IsPinned;
        item.IsPinned = newState;
        ApplyFilters();

        try
        {
            await catalog.SetPinnedAsync(gameId, newState, CancellationToken.None);
        }
        catch (Exception exception)
        {
            item.IsPinned = !newState;
            ApplyFilters();
            StatusMessage = $"Anheften fehlgeschlagen: {exception.Message}";
        }
    }

    [RelayCommand]
    private void NewGame()
    {
        SelectedGame = null;
        SelectedSession = null;
        DisplayName = string.Empty;
        ExecutablePath = string.Empty;
        Sessions.Clear();
        StatusMessage = "Neues Spiel: Name und EXE-Pfad eingeben.";
    }

    [RelayCommand]
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

    [RelayCommand]
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
                await catalog.UpdateGameAsync(
                    SelectedGame.Id,
                    DisplayName,
                    ExecutablePath,
                    SelectedGame.Model.DailyPlaytimeLimitMinutes,
                    SelectedGame.Model.WeeklyPlaytimeLimitMinutes,
                    SelectedGame.Model.BaselinePlaytimeMinutes,
                    SelectedGame.Model.Tags.Select(tag => tag.Tag).ToArray(),
                    CancellationToken.None);
            }

            await RefreshAsync();
            if (SelectedGame is { } saved)
            {
                DisplayName = saved.Name;
                ExecutablePath = saved.ExecutablePath;
            }

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

    private void RefreshMergeTargets()
    {
        var previousId = SelectedMergeTarget?.Id;
        MergeTargets.Clear();
        foreach (var candidate in allGames.Where(game => game.Id != selectedGame?.Id).OrderBy(game => game.Name))
        {
            MergeTargets.Add(candidate);
        }

        SelectedMergeTarget = MergeTargets.FirstOrDefault(game => game.Id == previousId);
        OnPropertyChanged(nameof(CanMergeSelectedGame));
    }

    [RelayCommand]
    private async Task MergeSelectedGameAsync()
    {
        if (SelectedGame is not { } source || SelectedMergeTarget is not { } target)
        {
            StatusMessage = "Bitte ein Spiel und ein Zielspiel auswählen.";
            return;
        }

        try
        {
            var result = await catalog.MergeGamesAsync(source.Id, target.Id, CancellationToken.None);
            SelectedGame = null;
            await RefreshAsync();
            SelectedGame = Games.FirstOrDefault(game => game.Id == result.TargetGameId);
            StatusMessage = result.CombinedSessionCount == 0
                ? $"{result.MovedSessionCount} Session(s) zu {result.TargetGameName} übernommen"
                : $"{result.MovedSessionCount} Session(s) zu {result.TargetGameName} übernommen, {result.CombinedSessionCount} davon zusammengefasst";
        }
        catch (YFTimeTrackerException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Zusammenführen fehlgeschlagen: {exception.Message}";
        }
    }

    [RelayCommand]
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

    [RelayCommand]
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

    [RelayCommand]
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

        var selectedSessionId = SelectedSession?.Id;
        Sessions.Clear();
        foreach (var session in storedSessions)
        {
            Sessions.Add(new SessionListItemViewModel(session, iconPath: game.IconPath));
        }

        SelectedSession = Sessions.FirstOrDefault(session => session.Id == selectedSessionId);
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
            IconPath = await gameIcons.GetGameImagePathAsync(
                game.Id,
                game.PrimaryExecutable?.ExecutablePath,
                CancellationToken.None)
        });
        var resolved = await Task.WhenAll(tasks);
        return resolved.ToDictionary(item => item.Id, item => item.IconPath);
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

public sealed record LibraryTagFilterOption(string? Tag, string Label);

public enum LibraryStatusFilterKind
{
    All,
    Pinned,
    Running,
    MissingExecutable
}

public enum LibrarySortKind
{
    LastPlayed,
    Playtime,
    Name
}
