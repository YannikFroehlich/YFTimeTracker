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

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore settings;
    private readonly IStartupService startupService;
    private readonly IBackupService backupService;
    private readonly IFilePickerService filePicker;
    private readonly IExplorerService explorerService;
    private readonly ThemeService themeService;
    private string? lastExportedFilePath;
    private readonly IGameTrackingService trackingService;
    private readonly IGameInstallationProvider installationProvider;
    private readonly IAppUpdateService appUpdateService;
    private readonly AppDiagnosticsService diagnosticsService;
    private readonly ITrackingDiagnosticLog trackingDiagnostics;
    private readonly ITrackingExclusionService? trackingExclusions;
    private readonly ICloudAuthService? cloudAuth;
    private readonly DispatcherQueue dispatcherQueue;
    private ThemeOption selectedTheme;
    private BackupDestinationOption selectedBackupDestination;
    private BackupListItemViewModel? selectedBackup;

    public SettingsViewModel(
        ISettingsStore settings,
        IStartupService startupService,
        IBackupService backupService,
        IFilePickerService filePicker,
        IExplorerService explorerService,
        ThemeService themeService,
        IGameTrackingService trackingService,
        IGameInstallationProvider installationProvider,
        IAppUpdateService appUpdateService,
        AppDiagnosticsService diagnosticsService,
        ITrackingDiagnosticLog trackingDiagnostics,
        ITrackingExclusionService? trackingExclusions = null,
        ICloudAuthService? cloudAuth = null)
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
        this.trackingExclusions = trackingExclusions;
        this.cloudAuth = cloudAuth;
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
            new BackupDestinationOption(BackupDestinationKind.YfDatabase, "YFDatenbank (Konto)")
        ];
        selectedBackupDestination = BackupDestinationOptions[0];


        ApplyUpdateState(appUpdateService.State);
        appUpdateService.StateChanged += AppUpdateService_StateChanged;
    }

    [ObservableProperty]
    public partial bool TrackingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool LauncherDiscoveryEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool MinimizeOnClose { get; set; } = true;

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    [ObservableProperty]
    public partial double TrackingIntervalSeconds { get; set; } = 3;

    [ObservableProperty]
    public partial double HeartbeatIntervalSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial double BackupRetentionDays { get; set; } = 14;

    [ObservableProperty]
    public partial string StartupStateText { get; private set; } = "Unbekannt";

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "Bereit";

    public IReadOnlyList<LauncherStatusItem> LauncherStatuses { get; } = Enum.GetValues<GameSource>()
        .Where(source => source != GameSource.Manual)
        .Select(source => new LauncherStatusItem(source))
        .ToArray();

    [ObservableProperty]
    public partial string CurrentAppVersionText { get; private set; } = "Installiert: unbekannt";

    [ObservableProperty]
    public partial string UpdateStatusText { get; private set; } = "Update-Status wird geladen …";

    [ObservableProperty]
    public partial string AvailableUpdateText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanCheckForUpdates { get; private set; }

    [ObservableProperty]
    public partial double UpdateProgress { get; private set; }

    [ObservableProperty]
    public partial Visibility UpdateProgressVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial Visibility InstallUpdateVisibility { get; private set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string DiagnosticsVersionText { get; private set; } = "Version wird geladen …";

    [ObservableProperty]
    public partial string DiagnosticsRuntimeText { get; private set; } = "Runtime wird geladen …";

    [ObservableProperty]
    public partial string DiagnosticsInstallDirectory { get; private set; } = "Installationsordner wird geladen …";

    [ObservableProperty]
    public partial string DiagnosticsDataDirectory { get; private set; } = "Datenordner wird geladen …";

    [ObservableProperty]
    public partial string DiagnosticsLogDirectory { get; private set; } = "Logordner wird geladen …";

    [ObservableProperty]
    public partial string TrackingDiagnosticsSummaryText { get; private set; } = "Noch keine Tracking-Ereignisse seit dem App-Start";

    public Visibility TrackingDiagnosticsVisibility => TrackingDiagnosticEvents.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility TrackingDiagnosticsEmptyVisibility => TrackingDiagnosticEvents.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    [ObservableProperty]
    public partial bool IsExportFolderAvailable { get; private set; }

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
                OnPropertyChanged(nameof(CloudBackupRowVisibility));
                OnPropertyChanged(nameof(CloudBackupHintText));
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExternalBackupFolderPathDisplay))]
    public partial string ExternalBackupFolderPath { get; private set; } = string.Empty;

    public string ExternalBackupFolderPathDisplay =>
        string.IsNullOrWhiteSpace(ExternalBackupFolderPath) ? "Kein Ordner ausgewählt" : ExternalBackupFolderPath;

    public Visibility ExternalFolderRowVisibility => SelectedBackupDestination.Value
        is BackupDestinationKind.OneDrive or BackupDestinationKind.GoogleDrive
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CloudBackupRowVisibility => SelectedBackupDestination.Value == BackupDestinationKind.YfDatabase
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string CloudBackupHintText => cloudAuth?.CurrentSession is { } session
        ? $"Die tägliche Sicherungsdatei wird nach dem Start zusätzlich im Konto {session.Email} abgelegt."
        : "Für dieses Sicherungsziel oben rechts im Profil anmelden.";

    public ObservableCollection<BackupListItemViewModel> Backups { get; } = [];

    public ObservableCollection<TrackingDiagnosticEventViewModel> TrackingDiagnosticEvents { get; } = [];

    public ObservableCollection<TrackingExclusionRuleViewModel> TrackingExclusionRules { get; } = [];

    public Visibility TrackingExclusionsVisibility => TrackingExclusionRules.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility TrackingExclusionsEmptyVisibility => TrackingExclusionRules.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

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

    [RelayCommand]
    public async Task LoadAsync()
    {
        ApplyUpdateState(appUpdateService.State);
        ApplyDiagnosticsSnapshot(diagnosticsService.GetSnapshot());
        RefreshTrackingDiagnostics();
        await RefreshTrackingExclusionsAsync();
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
        OnPropertyChanged(nameof(CloudBackupHintText));
        ExternalBackupFolderPath = await settings.GetAsync(AppSettingKeys.BackupExternalFolderPath, CancellationToken.None) ?? string.Empty;
        RefreshBackups();

        var state = await startupService.GetStateAsync(CancellationToken.None);
        StartWithWindows = state == StartupState.Enabled;
        StartupStateText = FormatStartupState(state);

        try
        {
            var launchers = await installationProvider.DiscoverAsync(CancellationToken.None);
            foreach (var launcher in LauncherStatuses)
            {
                launcher.Status = FormatLauncherState(launcher.Source, launchers);
            }
        }
        catch
        {
            foreach (var launcher in LauncherStatuses)
            {
                launcher.Status = "Prüfung fehlgeschlagen";
            }
        }
    }

    [RelayCommand]
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
        if (SelectedBackupDestination.Value == BackupDestinationKind.YfDatabase && cloudAuth?.IsSignedIn == true)
        {
            StatusMessage = await backupService.MirrorDailyBackupToCloudAsync(CancellationToken.None)
                ? "Einstellungen gespeichert, die aktuelle Sicherung liegt in der YFDatenbank"
                : "Einstellungen gespeichert, die Sicherung konnte nicht in die YFDatenbank übertragen werden (Details im Log)";
        }

        App.MainWindow?.SetMinimizeOnClose(MinimizeOnClose);
    }

    [RelayCommand]
    private async Task DownloadCloudBackupsAsync()
    {
        if (cloudAuth?.IsSignedIn != true)
        {
            StatusMessage = "Für Sicherungen aus der YFDatenbank oben rechts im Profil anmelden.";
            return;
        }

        try
        {
            var count = await backupService.DownloadCloudBackupsAsync(CancellationToken.None);
            StatusMessage = count switch
            {
                0 => "Alle Sicherungen aus der YFDatenbank liegen bereits lokal vor",
                1 => "1 Sicherung aus der YFDatenbank geladen",
                _ => $"{count} Sicherungen aus der YFDatenbank geladen"
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sicherungen konnten nicht geladen werden: {ex.Message}";
        }
        finally
        {
            RefreshBackups();
        }
    }

    [RelayCommand]
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

    [RelayCommand]
    private async Task ChooseBackupFolderAsync()
    {
        var path = await filePicker.PickBackupFolderAsync(CancellationToken.None);
        if (path is not null)
        {
            ExternalBackupFolderPath = path;
        }
    }

    [RelayCommand]
    private async Task AddExecutableExclusionAsync()
    {
        if (trackingExclusions is null)
        {
            StatusMessage = "Erkennungsausschlüsse sind momentan nicht verfügbar.";
            return;
        }

        var path = await filePicker.PickExecutableAsync(CancellationToken.None);
        if (path is null)
        {
            return;
        }

        await AddTrackingExclusionAsync(TrackingExclusionKind.Executable, path);
    }

    [RelayCommand]
    private async Task AddDirectoryExclusionAsync()
    {
        if (trackingExclusions is null)
        {
            StatusMessage = "Erkennungsausschlüsse sind momentan nicht verfügbar.";
            return;
        }

        var path = await filePicker.PickTrackingExclusionFolderAsync(CancellationToken.None);
        if (path is null)
        {
            return;
        }

        await AddTrackingExclusionAsync(TrackingExclusionKind.Directory, path);
    }

    private async Task AddTrackingExclusionAsync(TrackingExclusionKind kind, string path)
    {
        try
        {
            await trackingExclusions!.AddAsync(kind, path, CancellationToken.None);
            await RefreshTrackingExclusionsAsync();
            await trackingService.ScanOnceAsync(CancellationToken.None);
            StatusMessage = kind == TrackingExclusionKind.Executable
                ? "EXE wird ab sofort nicht mehr getrackt"
                : "Prozesse in diesem Ordner werden ab sofort nicht mehr getrackt";
        }
        catch (YFTimeTrackerException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ausschluss konnte nicht gespeichert werden: {exception.Message}";
        }
    }

    public async Task RemoveTrackingExclusionAsync(long id)
    {
        if (trackingExclusions is null)
        {
            return;
        }

        try
        {
            await trackingExclusions.DeleteAsync(id, CancellationToken.None);
            await RefreshTrackingExclusionsAsync();
            await trackingService.ScanOnceAsync(CancellationToken.None);
            StatusMessage = "Erkennungsausschluss entfernt";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ausschluss konnte nicht entfernt werden: {exception.Message}";
        }
    }

    private async Task RefreshTrackingExclusionsAsync()
    {
        TrackingExclusionRules.Clear();
        if (trackingExclusions is not null)
        {
            foreach (var rule in await trackingExclusions.GetRulesAsync(CancellationToken.None))
            {
                TrackingExclusionRules.Add(new TrackingExclusionRuleViewModel(
                    rule.Id,
                    rule.Kind == TrackingExclusionKind.Executable ? "EXE" : "ORDNER",
                    rule.Value));
            }
        }

        OnPropertyChanged(nameof(TrackingExclusionsVisibility));
        OnPropertyChanged(nameof(TrackingExclusionsEmptyVisibility));
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

    [RelayCommand]
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

    [RelayCommand]
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

    [RelayCommand]
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

    [RelayCommand]
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
            : state.DownloadSizeText is { Length: > 0 } size
                ? $"Verfügbar: v{state.AvailableVersion} · {size}"
                : $"Verfügbar: v{state.AvailableVersion}";
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
        TrackingDiagnosticSeverity.Success => "#6FD49A",
        TrackingDiagnosticSeverity.Warning => "#F0C36A",
        TrackingDiagnosticSeverity.Error => "#FF7B70",
        _ => "#FF6A2B"
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

public sealed record TrackingExclusionRuleViewModel(long Id, string KindText, string Path);

public sealed partial class LauncherStatusItem(GameSource source) : ObservableObject
{

    public GameSource Source { get; } = source;

    public string Name { get; } = GameSourceNames.Name(source);

    [ObservableProperty]
    public partial string Status { get; set; } = "Nicht geprüft";
}
