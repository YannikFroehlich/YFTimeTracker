using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore settings;
    private readonly IStartupService startupService;
    private readonly IBackupService backupService;
    private readonly IFilePickerService filePicker;
    private readonly IExplorerService explorerService;
    private readonly IThemeService themeService;
    private string? lastExportedFilePath;
    private readonly IGameTrackingService trackingService;
    private readonly IGameInstallationProvider installationProvider;
    private readonly IAppUpdateService appUpdateService;
    private readonly IAppDiagnosticsService diagnosticsService;
    private readonly ITrackingDiagnosticLog trackingDiagnostics;
    private readonly DispatcherQueue dispatcherQueue;
    private bool trackingEnabled = true;
    private bool launcherDiscoveryEnabled = true;
    private bool minimizeOnClose = true;
    private bool startWithWindows;
    private bool startMinimized;
    private double trackingIntervalSeconds = 3;
    private double heartbeatIntervalSeconds = 30;
    private double backupRetentionDays = 14;
    private string startupStateText = "Unbekannt";
    private string statusMessage = "Bereit";
    private string steamStatusText = "Nicht geprüft";
    private string epicStatusText = "Nicht geprüft";
    private string gogStatusText = "Nicht geprüft";
    private string xboxStatusText = "Nicht geprüft";
    private string battleNetStatusText = "Nicht geprüft";
    private string ubisoftStatusText = "Nicht geprüft";
    private string eaAppStatusText = "Nicht geprüft";
    private string currentAppVersionText = "Installiert: unbekannt";
    private string updateStatusText = "Update-Status wird geladen …";
    private string availableUpdateText = string.Empty;
    private bool canCheckForUpdates;
    private double updateProgress;
    private Visibility updateProgressVisibility = Visibility.Collapsed;
    private Visibility installUpdateVisibility = Visibility.Collapsed;
    private string diagnosticsVersionText = "Version wird geladen …";
    private string diagnosticsRuntimeText = "Runtime wird geladen …";
    private string diagnosticsInstallDirectory = "Installationsordner wird geladen …";
    private string diagnosticsDataDirectory = "Datenordner wird geladen …";
    private string diagnosticsLogDirectory = "Logordner wird geladen …";
    private string trackingDiagnosticsSummaryText = "Noch keine Tracking-Ereignisse seit dem App-Start";
    private bool isExportFolderAvailable;
    private ThemeOption selectedTheme;
    private BackupDestinationOption selectedBackupDestination;
    private string externalBackupFolderPath = string.Empty;
    private BackupListItemViewModel? selectedBackup;

    public SettingsViewModel(
        ISettingsStore settings,
        IStartupService startupService,
        IBackupService backupService,
        IFilePickerService filePicker,
        IExplorerService explorerService,
        IThemeService themeService,
        IGameTrackingService trackingService,
        IGameInstallationProvider installationProvider,
        IAppUpdateService appUpdateService,
        IAppDiagnosticsService diagnosticsService,
        ITrackingDiagnosticLog trackingDiagnostics)
    {
        this.settings = settings;
        this.startupService = startupService;
        this.backupService = backupService;
        this.filePicker = filePicker;
        this.explorerService = explorerService;
        this.themeService = themeService;
        this.trackingService = trackingService;
        this.installationProvider = installationProvider;
        this.appUpdateService = appUpdateService;
        this.diagnosticsService = diagnosticsService;
        this.trackingDiagnostics = trackingDiagnostics;
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ThemeOptions =
        [
            new ThemeOption(AppThemePreference.System, "System"),
            new ThemeOption(AppThemePreference.Light, "Hell"),
            new ThemeOption(AppThemePreference.Dark, "Dunkel")
        ];
        selectedTheme = ThemeOptions[2];

        BackupDestinationOptions =
        [
            new BackupDestinationOption(BackupDestinationKind.Local, "Lokal"),
            new BackupDestinationOption(BackupDestinationKind.OneDrive, "OneDrive-Ordner"),
            new BackupDestinationOption(BackupDestinationKind.GoogleDrive, "Google Drive-Ordner"),
            new BackupDestinationOption(BackupDestinationKind.YfDatabase, "YFDatenbank (Demnächst)")
        ];
        selectedBackupDestination = BackupDestinationOptions[0];

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        OpenLogDirectoryCommand = new RelayCommand(OpenLogDirectory);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync);
        ClearTrackingDiagnosticsCommand = new RelayCommand(ClearTrackingDiagnostics);
        OpenExportFolderCommand = new RelayCommand(OpenExportFolder);
        ChooseBackupFolderCommand = new AsyncRelayCommand(ChooseBackupFolderAsync);

        ApplyUpdateState(appUpdateService.State);
        appUpdateService.StateChanged += AppUpdateService_StateChanged;
    }

    public bool TrackingEnabled
    {
        get => trackingEnabled;
        set => SetProperty(ref trackingEnabled, value);
    }

    public bool LauncherDiscoveryEnabled
    {
        get => launcherDiscoveryEnabled;
        set => SetProperty(ref launcherDiscoveryEnabled, value);
    }

    public bool MinimizeOnClose
    {
        get => minimizeOnClose;
        set => SetProperty(ref minimizeOnClose, value);
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set => SetProperty(ref startWithWindows, value);
    }

    public bool StartMinimized
    {
        get => startMinimized;
        set => SetProperty(ref startMinimized, value);
    }

    public double TrackingIntervalSeconds
    {
        get => trackingIntervalSeconds;
        set => SetProperty(ref trackingIntervalSeconds, value);
    }

    public double HeartbeatIntervalSeconds
    {
        get => heartbeatIntervalSeconds;
        set => SetProperty(ref heartbeatIntervalSeconds, value);
    }

    public double BackupRetentionDays
    {
        get => backupRetentionDays;
        set => SetProperty(ref backupRetentionDays, value);
    }

    public string StartupStateText
    {
        get => startupStateText;
        private set => SetProperty(ref startupStateText, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string SteamStatusText
    {
        get => steamStatusText;
        private set => SetProperty(ref steamStatusText, value);
    }

    public string EpicStatusText
    {
        get => epicStatusText;
        private set => SetProperty(ref epicStatusText, value);
    }

    public string GogStatusText
    {
        get => gogStatusText;
        private set => SetProperty(ref gogStatusText, value);
    }

    public string XboxStatusText
    {
        get => xboxStatusText;
        private set => SetProperty(ref xboxStatusText, value);
    }

    public string BattleNetStatusText
    {
        get => battleNetStatusText;
        private set => SetProperty(ref battleNetStatusText, value);
    }

    public string UbisoftStatusText
    {
        get => ubisoftStatusText;
        private set => SetProperty(ref ubisoftStatusText, value);
    }

    public string EaAppStatusText
    {
        get => eaAppStatusText;
        private set => SetProperty(ref eaAppStatusText, value);
    }

    public string CurrentAppVersionText
    {
        get => currentAppVersionText;
        private set => SetProperty(ref currentAppVersionText, value);
    }

    public string UpdateStatusText
    {
        get => updateStatusText;
        private set => SetProperty(ref updateStatusText, value);
    }

    public string AvailableUpdateText
    {
        get => availableUpdateText;
        private set => SetProperty(ref availableUpdateText, value);
    }

    public bool CanCheckForUpdates
    {
        get => canCheckForUpdates;
        private set => SetProperty(ref canCheckForUpdates, value);
    }

    public double UpdateProgress
    {
        get => updateProgress;
        private set => SetProperty(ref updateProgress, value);
    }

    public Visibility UpdateProgressVisibility
    {
        get => updateProgressVisibility;
        private set => SetProperty(ref updateProgressVisibility, value);
    }

    public Visibility InstallUpdateVisibility
    {
        get => installUpdateVisibility;
        private set => SetProperty(ref installUpdateVisibility, value);
    }

    public string DiagnosticsVersionText
    {
        get => diagnosticsVersionText;
        private set => SetProperty(ref diagnosticsVersionText, value);
    }

    public string DiagnosticsRuntimeText
    {
        get => diagnosticsRuntimeText;
        private set => SetProperty(ref diagnosticsRuntimeText, value);
    }

    public string DiagnosticsInstallDirectory
    {
        get => diagnosticsInstallDirectory;
        private set => SetProperty(ref diagnosticsInstallDirectory, value);
    }

    public string DiagnosticsDataDirectory
    {
        get => diagnosticsDataDirectory;
        private set => SetProperty(ref diagnosticsDataDirectory, value);
    }

    public string DiagnosticsLogDirectory
    {
        get => diagnosticsLogDirectory;
        private set => SetProperty(ref diagnosticsLogDirectory, value);
    }

    public string TrackingDiagnosticsSummaryText
    {
        get => trackingDiagnosticsSummaryText;
        private set => SetProperty(ref trackingDiagnosticsSummaryText, value);
    }

    public Visibility TrackingDiagnosticsVisibility => TrackingDiagnosticEvents.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility TrackingDiagnosticsEmptyVisibility => TrackingDiagnosticEvents.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsExportFolderAvailable { get => isExportFolderAvailable; private set => SetProperty(ref isExportFolderAvailable, value); }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public ThemeOption SelectedTheme { get => selectedTheme; set => SetProperty(ref selectedTheme, value); }

    public IReadOnlyList<BackupDestinationOption> BackupDestinationOptions { get; }

    public BackupDestinationOption SelectedBackupDestination
    {
        get => selectedBackupDestination;
        set
        {
            if (SetProperty(ref selectedBackupDestination, value))
            {
                OnPropertyChanged(nameof(ExternalFolderRowVisibility));
                OnPropertyChanged(nameof(YfDatabasePreviewNoticeVisibility));
            }
        }
    }

    public string ExternalBackupFolderPath
    {
        get => externalBackupFolderPath;
        private set
        {
            if (SetProperty(ref externalBackupFolderPath, value))
            {
                OnPropertyChanged(nameof(ExternalBackupFolderPathDisplay));
            }
        }
    }

    public string ExternalBackupFolderPathDisplay =>
        string.IsNullOrWhiteSpace(ExternalBackupFolderPath) ? "Kein Ordner ausgewählt" : ExternalBackupFolderPath;

    public Visibility ExternalFolderRowVisibility => SelectedBackupDestination.Value
        is BackupDestinationKind.OneDrive or BackupDestinationKind.GoogleDrive
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility YfDatabasePreviewNoticeVisibility => SelectedBackupDestination.Value == BackupDestinationKind.YfDatabase
        ? Visibility.Visible
        : Visibility.Collapsed;

    public IAsyncRelayCommand ChooseBackupFolderCommand { get; }

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ExportCommand { get; }

    public ObservableCollection<BackupListItemViewModel> Backups { get; } = [];

    public ObservableCollection<TrackingDiagnosticEventViewModel> TrackingDiagnosticEvents { get; } = [];

    public BackupListItemViewModel? SelectedBackup
    {
        get => selectedBackup;
        set
        {
            if (SetProperty(ref selectedBackup, value))
            {
                OnPropertyChanged(nameof(CanRestoreSelectedBackup));
            }
        }
    }

    public bool CanRestoreSelectedBackup => selectedBackup is not null;

    public Visibility BackupsVisibility => Backups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BackupsEmptyVisibility => Backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public IRelayCommand OpenLogDirectoryCommand { get; }

    public IAsyncRelayCommand ExportDiagnosticsCommand { get; }

    public IRelayCommand ClearTrackingDiagnosticsCommand { get; }

    public IRelayCommand OpenExportFolderCommand { get; }

    public async Task LoadAsync()
    {
        ApplyUpdateState(appUpdateService.State);
        ApplyDiagnosticsSnapshot(diagnosticsService.GetSnapshot());
        RefreshTrackingDiagnostics();
        TrackingEnabled = await settings.GetBoolAsync(AppSettingKeys.TrackingEnabled, true, CancellationToken.None);
        LauncherDiscoveryEnabled = await settings.GetBoolAsync(AppSettingKeys.LauncherDiscoveryEnabled, true, CancellationToken.None);
        MinimizeOnClose = await settings.GetBoolAsync(AppSettingKeys.MinimizeOnClose, true, CancellationToken.None);
        StartMinimized = await settings.GetBoolAsync(AppSettingKeys.StartMinimized, false, CancellationToken.None);
        TrackingIntervalSeconds = await settings.GetIntAsync(AppSettingKeys.TrackingIntervalSeconds, 3, CancellationToken.None);
        HeartbeatIntervalSeconds = await settings.GetIntAsync(AppSettingKeys.HeartbeatIntervalSeconds, 30, CancellationToken.None);
        BackupRetentionDays = await settings.GetIntAsync(AppSettingKeys.BackupRetentionDays, 14, CancellationToken.None);
        SelectedTheme = ThemeOptions.FirstOrDefault(option => option.Value == themeService.CurrentPreference) ?? ThemeOptions[2];
        var destinationRaw = await settings.GetAsync(AppSettingKeys.BackupDestination, CancellationToken.None);
        var destination = Enum.TryParse<BackupDestinationKind>(destinationRaw, out var parsedDestination)
            ? parsedDestination
            : BackupDestinationKind.Local;
        SelectedBackupDestination = BackupDestinationOptions.FirstOrDefault(option => option.Value == destination)
            ?? BackupDestinationOptions[0];
        ExternalBackupFolderPath = await settings.GetAsync(AppSettingKeys.BackupExternalFolderPath, CancellationToken.None) ?? string.Empty;
        RefreshBackups();

        var state = await startupService.GetStateAsync(CancellationToken.None);
        StartWithWindows = state == StartupState.Enabled;
        StartupStateText = FormatStartupState(state);

        try
        {
            var launchers = await installationProvider.DiscoverAsync(CancellationToken.None);
            SteamStatusText = FormatLauncherState(GameSource.Steam, launchers);
            EpicStatusText = FormatLauncherState(GameSource.Epic, launchers);
            GogStatusText = FormatLauncherState(GameSource.Gog, launchers);
            XboxStatusText = FormatLauncherState(GameSource.Xbox, launchers);
            BattleNetStatusText = FormatLauncherState(GameSource.BattleNet, launchers);
            UbisoftStatusText = FormatLauncherState(GameSource.Ubisoft, launchers);
            EaAppStatusText = FormatLauncherState(GameSource.EaApp, launchers);
        }
        catch
        {
            SteamStatusText = EpicStatusText = GogStatusText = XboxStatusText =
                BattleNetStatusText = UbisoftStatusText = EaAppStatusText = "Prüfung fehlgeschlagen";
        }
    }

    private async Task SaveAsync()
    {
        await settings.SetAsync(AppSettingKeys.TrackingEnabled, TrackingEnabled.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.LauncherDiscoveryEnabled, LauncherDiscoveryEnabled.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.MinimizeOnClose, MinimizeOnClose.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.StartMinimized, StartMinimized.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, Math.Clamp((int)TrackingIntervalSeconds, 1, 60).ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.HeartbeatIntervalSeconds, Math.Clamp((int)HeartbeatIntervalSeconds, 5, 300).ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.BackupRetentionDays, Math.Clamp((int)BackupRetentionDays, 1, 365).ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.BackupDestination, SelectedBackupDestination.Value.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.BackupExternalFolderPath, ExternalBackupFolderPath, CancellationToken.None);
        await themeService.SetThemeAsync(SelectedTheme.Value, CancellationToken.None);

        var startupState = await startupService.SetEnabledAsync(StartWithWindows, CancellationToken.None);
        StartupStateText = FormatStartupState(startupState);

        if (TrackingEnabled && trackingService.State.IsPaused)
        {
            await trackingService.ResumeAsync(CancellationToken.None);
        }
        else if (!TrackingEnabled && !trackingService.State.IsPaused)
        {
            await trackingService.PauseAsync(CancellationToken.None);
        }
        else
        {
            // Übernimmt ein geändertes Scan-Intervall sofort, statt bis zum nächsten Tick zu warten.
            await trackingService.ScanOnceAsync(CancellationToken.None);
        }

        StatusMessage = "Einstellungen gespeichert";
        App.MainWindow?.SetMinimizeOnClose(MinimizeOnClose);
    }

    private async Task ExportAsync()
    {
        var path = await filePicker.PickExportArchiveAsync(CancellationToken.None);
        if (path is null)
        {
            return;
        }

        var result = await backupService.ExportAsync(path, CancellationToken.None);
        StatusMessage = $"Export gespeichert: {result.GameCount} Spiele, {result.SessionCount} Sessions";
        SetExportedFile(path);
    }

    public Task<string?> PickImportArchiveAsync()
    {
        return filePicker.PickImportArchiveAsync(CancellationToken.None);
    }

    private async Task ChooseBackupFolderAsync()
    {
        var path = await filePicker.PickBackupFolderAsync(CancellationToken.None);
        if (path is not null)
        {
            ExternalBackupFolderPath = path;
        }
    }

    public async Task ImportAsync(string archivePath)
    {
        try
        {
            var result = await ImportBackupAsync(backupService, trackingService, archivePath, CancellationToken.None);
            StatusMessage = $"Import abgeschlossen: {result.GameCount} Spiele, {result.SessionCount} Sessions";
        }
        catch (YFTimeTrackerException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            // Der Import legt vorher eine Sicherheitskopie an; die soll sofort in der Liste stehen.
            RefreshBackups();
        }
    }

    public async Task RestoreSelectedBackupAsync()
    {
        if (SelectedBackup is not { } backup)
        {
            StatusMessage = "Bitte zuerst eine Sicherung auswählen.";
            return;
        }

        try
        {
            await RestoreBackupAsync(backupService, trackingService, backup.FilePath, CancellationToken.None);
            StatusMessage = $"Sicherung vom {backup.CreatedText} wiederhergestellt";
            await LoadAsync();
        }
        catch (YFTimeTrackerException ex)
        {
            StatusMessage = ex.Message;
            RefreshBackups();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Wiederherstellen fehlgeschlagen: {ex.Message}";
            RefreshBackups();
        }
    }

    internal static async Task<RestoreResult> RestoreBackupAsync(
        IBackupService backupService,
        IGameTrackingService trackingService,
        string backupPath,
        CancellationToken cancellationToken)
    {
        var wasPaused = trackingService.State.IsPaused;
        try
        {
            // Wie beim Import: das Tracking darf die Datenbank nicht offen halten, wenn sie
            // ausgetauscht wird, und eine laufende Session gehört nicht in den neuen Stand.
            await trackingService.PauseAsync(cancellationToken);
            return await backupService.RestoreAsync(backupPath, cancellationToken);
        }
        finally
        {
            if (!wasPaused)
            {
                await trackingService.ResumeAsync(CancellationToken.None);
            }
        }
    }

    private void RefreshBackups()
    {
        var previousPath = SelectedBackup?.FilePath;
        Backups.Clear();
        foreach (var backup in backupService.GetBackups())
        {
            Backups.Add(new BackupListItemViewModel(backup));
        }

        SelectedBackup = Backups.FirstOrDefault(item =>
            string.Equals(item.FilePath, previousPath, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(BackupsVisibility));
        OnPropertyChanged(nameof(BackupsEmptyVisibility));
    }

    internal static async Task<ImportResult> ImportBackupAsync(
        IBackupService backupService,
        IGameTrackingService trackingService,
        string path,
        CancellationToken cancellationToken)
    {
        var wasPaused = trackingService.State.IsPaused;
        try
        {
            await trackingService.PauseAsync(cancellationToken);
            return await backupService.ImportAsync(path, cancellationToken);
        }
        finally
        {
            if (!wasPaused)
            {
                await trackingService.ResumeAsync(CancellationToken.None);
            }
        }
    }

    private void OpenLogDirectory()
    {
        try
        {
            diagnosticsService.OpenLogDirectory();
            StatusMessage = "Logordner wurde geöffnet";
        }
        catch
        {
            StatusMessage = "Logordner konnte nicht geöffnet werden";
        }
    }

    private async Task ExportDiagnosticsAsync()
    {
        var path = await filePicker.PickDiagnosticsArchiveAsync(CancellationToken.None);
        if (path is null)
        {
            return;
        }

        try
        {
            StatusMessage = "Diagnosebericht wird erstellt …";
            var result = await diagnosticsService.ExportAsync(path, CancellationToken.None);
            StatusMessage = $"Diagnosebericht gespeichert: {Path.GetFileName(result.ArchivePath)} · {result.IncludedLogCount} Logdatei(en)";
            SetExportedFile(result.ArchivePath);
        }
        catch
        {
            StatusMessage = "Diagnosebericht konnte nicht erstellt werden";
        }
    }

    public void RefreshTrackingDiagnostics()
    {
        var allEvents = trackingDiagnostics.GetRecentEvents();
        var recentEvents = allEvents.Take(50).ToArray();
        if (TrackingDiagnosticEvents.Select(item => item.Sequence).SequenceEqual(recentEvents.Select(item => item.Sequence)))
        {
            return;
        }

        TrackingDiagnosticEvents.Clear();
        foreach (var trackingEvent in recentEvents)
        {
            var occurredAtLocal = TimeZoneInfo.ConvertTime(trackingEvent.OccurredAtUtc, TimeZoneInfo.Local);
            TrackingDiagnosticEvents.Add(new TrackingDiagnosticEventViewModel(
                trackingEvent.Sequence,
                occurredAtLocal.Date == DateTime.Today
                    ? occurredAtLocal.ToString("HH:mm:ss")
                    : occurredAtLocal.ToString("dd.MM. HH:mm"),
                FormatTrackingEventKind(trackingEvent.Kind),
                trackingEvent.Title,
                trackingEvent.Detail,
                GetTrackingEventColor(trackingEvent.Severity),
                GetTrackingEventGlyph(trackingEvent.Severity)));
        }

        TrackingDiagnosticsSummaryText = allEvents.Count == 0
            ? "Noch keine Tracking-Ereignisse seit dem App-Start"
            : allEvents.Count > recentEvents.Length
                ? $"Neueste {recentEvents.Length} von {allEvents.Count} Ereignissen seit dem App-Start"
                : $"{recentEvents.Length} {(recentEvents.Length == 1 ? "Ereignis" : "Ereignisse")} seit dem App-Start · neueste zuerst";
        OnPropertyChanged(nameof(TrackingDiagnosticsVisibility));
        OnPropertyChanged(nameof(TrackingDiagnosticsEmptyVisibility));
    }

    private void ClearTrackingDiagnostics()
    {
        trackingDiagnostics.Clear();
        RefreshTrackingDiagnostics();
        StatusMessage = "Tracking-Ereignisse wurden geleert";
    }

    private void SetExportedFile(string path)
    {
        lastExportedFilePath = path;
        IsExportFolderAvailable = true;
    }

    private void OpenExportFolder()
    {
        if (lastExportedFilePath is not null)
        {
            explorerService.RevealFile(lastExportedFilePath);
        }
    }

    private static string FormatStartupState(StartupState state)
    {
        return state switch
        {
            StartupState.Enabled => "Aktiv",
            StartupState.Disabled => "Aus",
            StartupState.DisabledByPolicy => "Durch Richtlinie deaktiviert",
            _ => "Nicht verfügbar"
        };
    }

    private static string FormatLauncherState(GameSource source, LauncherDiscoveryResult result)
    {
        var state = result.Sources.GetValueOrDefault(source, LauncherAvailability.NotInstalled);
        return state switch
        {
            LauncherAvailability.Available => $"Erkannt · {result.Games.Count(game => game.Source == source)} Installation(en)",
            LauncherAvailability.Error => "Lesefehler",
            _ => "Nicht installiert"
        };
    }

    private void AppUpdateService_StateChanged(object? sender, AppUpdateState state)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            ApplyUpdateState(state);
            return;
        }

        dispatcherQueue.TryEnqueue(() => ApplyUpdateState(state));
    }

    private void ApplyUpdateState(AppUpdateState state)
    {
        CurrentAppVersionText = $"Installiert: v{state.CurrentVersion}";
        UpdateStatusText = state.Message;
        AvailableUpdateText = state.AvailableVersion is null
            ? "Stabiler Release-Kanal · GitHub"
            : $"Verfügbar: v{state.AvailableVersion}{FormatDownloadSize(state.DownloadSize)}";
        CanCheckForUpdates = state.CanCheckForUpdates;
        UpdateProgress = state.DownloadProgress;
        UpdateProgressVisibility = state.Stage == AppUpdateStage.Downloading
            ? Visibility.Visible
            : Visibility.Collapsed;
        InstallUpdateVisibility = state.HasAvailableUpdate
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyDiagnosticsSnapshot(AppDiagnosticsSnapshot snapshot)
    {
        DiagnosticsVersionText = $"v{snapshot.Version} · {snapshot.Distribution}";
        DiagnosticsRuntimeText = $"{snapshot.RuntimeDescription} · {snapshot.ProcessArchitecture} · {snapshot.OperatingSystemDescription}";
        DiagnosticsInstallDirectory = snapshot.InstallDirectory;
        DiagnosticsDataDirectory = snapshot.DataDirectory;
        DiagnosticsLogDirectory = snapshot.LogDirectory;
    }

    private static string FormatDownloadSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        return bytes >= 1024L * 1024L
            ? $" · {bytes / (1024d * 1024d):0.#} MB"
            : $" · {bytes / 1024d:0.#} KB";
    }

    private static string FormatTrackingEventKind(TrackingDiagnosticEventKind kind) => kind switch
    {
        TrackingDiagnosticEventKind.Detection => "ERKENNUNG",
        TrackingDiagnosticEventKind.Assignment => "ZUORDNUNG",
        TrackingDiagnosticEventKind.Exclusion => "AUSGESCHLOSSEN",
        TrackingDiagnosticEventKind.Session => "SESSION",
        TrackingDiagnosticEventKind.Interruption => "UNTERBRECHUNG",
        TrackingDiagnosticEventKind.Error => "FEHLER",
        _ => "STATUS"
    };

    private static string GetTrackingEventColor(TrackingDiagnosticSeverity severity) => severity switch
    {
        TrackingDiagnosticSeverity.Success => "#29E7A4",
        TrackingDiagnosticSeverity.Warning => "#F5B942",
        TrackingDiagnosticSeverity.Error => "#FF6B7A",
        _ => "#3182FF"
    };

    private static string GetTrackingEventGlyph(TrackingDiagnosticSeverity severity) => severity switch
    {
        TrackingDiagnosticSeverity.Success => "\uE73E",
        TrackingDiagnosticSeverity.Warning => "\uE7BA",
        TrackingDiagnosticSeverity.Error => "\uE783",
        _ => "\uE946"
    };
}

public sealed record ThemeOption(AppThemePreference Value, string Label);

public sealed record BackupDestinationOption(BackupDestinationKind Value, string Label);

public sealed record TrackingDiagnosticEventViewModel(
    long Sequence,
    string TimeText,
    string KindText,
    string Title,
    string Detail,
    string AccentColor,
    string Glyph);
