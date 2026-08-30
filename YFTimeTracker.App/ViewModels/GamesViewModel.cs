using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.App.ViewModels;

public sealed class GamesViewModel : ObservableObject
{
    private readonly IGameCatalogService catalog;
    private readonly IGameSessionRepository sessions;
    private readonly IGameSessionEditor sessionEditor;
    private readonly IFilePickerService filePicker;
    private GameListItemViewModel? selectedGame;
    private SessionListItemViewModel? selectedSession;
    private string displayName = string.Empty;
    private string executablePath = string.Empty;
    private string manualStartText = DateTime.Now.AddHours(-1).ToString("g", CultureInfo.CurrentCulture);
    private string manualEndText = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
    private string statusMessage = "Bereit";

    public GamesViewModel(
        IGameCatalogService catalog,
        IGameSessionRepository sessions,
        IGameSessionEditor sessionEditor,
        IFilePickerService filePicker)
    {
        this.catalog = catalog;
        this.sessions = sessions;
        this.sessionEditor = sessionEditor;
        this.filePicker = filePicker;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        NewGameCommand = new RelayCommand(NewGame);
        BrowseExecutableCommand = new AsyncRelayCommand(BrowseExecutableAsync);
        AddOrUpdateGameCommand = new AsyncRelayCommand(AddOrUpdateGameAsync);
        DeleteSelectedGameCommand = new AsyncRelayCommand(DeleteSelectedGameAsync);
        AddManualSessionCommand = new AsyncRelayCommand(AddManualSessionAsync);
        DeleteSelectedSessionCommand = new AsyncRelayCommand(DeleteSelectedSessionAsync);
    }

    public ObservableCollection<GameListItemViewModel> Games { get; } = [];

    public ObservableCollection<SessionListItemViewModel> Sessions { get; } = [];

    public GameListItemViewModel? SelectedGame
    {
        get => selectedGame;
        set
        {
            if (SetProperty(ref selectedGame, value) && value is not null)
            {
                DisplayName = value.Name;
                ExecutablePath = value.ExecutablePath;
                _ = LoadSessionsAsync();
            }
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

    public IRelayCommand NewGameCommand { get; }

    public IAsyncRelayCommand BrowseExecutableCommand { get; }

    public IAsyncRelayCommand AddOrUpdateGameCommand { get; }

    public IAsyncRelayCommand DeleteSelectedGameCommand { get; }

    public IAsyncRelayCommand AddManualSessionCommand { get; }

    public IAsyncRelayCommand DeleteSelectedSessionCommand { get; }

    public async Task RefreshAsync()
    {
        var games = await catalog.GetGamesAsync(CancellationToken.None);
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(new GameListItemViewModel(game));
        }

        if (SelectedGame is not null)
        {
            SelectedGame = Games.FirstOrDefault(game => game.Id == SelectedGame.Id);
        }

        if (SelectedGame is null)
        {
            Sessions.Clear();
        }

        StatusMessage = $"{Games.Count} Spiel(e) registriert";
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
        Sessions.Clear();
        if (SelectedGame is null)
        {
            return;
        }

        var storedSessions = await sessions.GetSessionsForGameAsync(SelectedGame.Id, CancellationToken.None);
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
