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
    private readonly IGameTrackingService trackingService;
    private readonly IGameInstallationProvider installationProvider;
    private readonly IAppUpdateService appUpdateService;
    private readonly DispatcherQueue dispatcherQueue;
    private bool trackingEnabled = true;
    private bool launcherDiscoveryEnabled = true;
    private bool minimizeOnClose = true;
    private bool startWithWindows;
    private double trackingIntervalSeconds = 3;
    private double heartbeatIntervalSeconds = 30;
    private double backupRetentionDays = 14;
    private string startupStateText = "Unbekannt";
    private string statusMessage = "Bereit";
    private string steamStatusText = "Nicht geprüft";
    private string epicStatusText = "Nicht geprüft";
    private string gogStatusText = "Nicht geprüft";
    private string currentAppVersionText = "Installiert: unbekannt";
    private string updateStatusText = "Update-Status wird geladen …";
    private string availableUpdateText = string.Empty;
    private bool canCheckForUpdates;
    private double updateProgress;
    private Visibility updateProgressVisibility = Visibility.Collapsed;
    private Visibility installUpdateVisibility = Visibility.Collapsed;

    public SettingsViewModel(
        ISettingsStore settings,
        IStartupService startupService,
        IBackupService backupService,
        IFilePickerService filePicker,
        IGameTrackingService trackingService,
        IGameInstallationProvider installationProvider,
        IAppUpdateService appUpdateService)
    {
        this.settings = settings;
        this.startupService = startupService;
        this.backupService = backupService;
        this.filePicker = filePicker;
        this.trackingService = trackingService;
        this.installationProvider = installationProvider;
        this.appUpdateService = appUpdateService;
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);

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

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ExportCommand { get; }

    public IAsyncRelayCommand ImportCommand { get; }

    public async Task LoadAsync()
    {
        ApplyUpdateState(appUpdateService.State);
        TrackingEnabled = await settings.GetBoolAsync(AppSettingKeys.TrackingEnabled, true, CancellationToken.None);
        LauncherDiscoveryEnabled = await settings.GetBoolAsync(AppSettingKeys.LauncherDiscoveryEnabled, true, CancellationToken.None);
        MinimizeOnClose = await settings.GetBoolAsync(AppSettingKeys.MinimizeOnClose, true, CancellationToken.None);
        TrackingIntervalSeconds = await settings.GetIntAsync(AppSettingKeys.TrackingIntervalSeconds, 3, CancellationToken.None);
        HeartbeatIntervalSeconds = await settings.GetIntAsync(AppSettingKeys.HeartbeatIntervalSeconds, 30, CancellationToken.None);
        BackupRetentionDays = await settings.GetIntAsync(AppSettingKeys.BackupRetentionDays, 14, CancellationToken.None);

        var state = await startupService.GetStateAsync(CancellationToken.None);
        StartWithWindows = state == StartupState.Enabled;
        StartupStateText = FormatStartupState(state);

        try
        {
            var launchers = await installationProvider.DiscoverAsync(CancellationToken.None);
            SteamStatusText = FormatLauncherState(GameSource.Steam, launchers);
            EpicStatusText = FormatLauncherState(GameSource.Epic, launchers);
            GogStatusText = FormatLauncherState(GameSource.Gog, launchers);
        }
        catch
        {
            SteamStatusText = EpicStatusText = GogStatusText = "Prüfung fehlgeschlagen";
        }
    }

    private async Task SaveAsync()
    {
        await settings.SetAsync(AppSettingKeys.TrackingEnabled, TrackingEnabled.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.LauncherDiscoveryEnabled, LauncherDiscoveryEnabled.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.MinimizeOnClose, MinimizeOnClose.ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.TrackingIntervalSeconds, Math.Clamp((int)TrackingIntervalSeconds, 1, 60).ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.HeartbeatIntervalSeconds, Math.Clamp((int)HeartbeatIntervalSeconds, 5, 300).ToString(), CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.BackupRetentionDays, Math.Clamp((int)BackupRetentionDays, 1, 365).ToString(), CancellationToken.None);

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
    }

    private async Task ImportAsync()
    {
        var path = await filePicker.PickImportArchiveAsync(CancellationToken.None);
        if (path is null)
        {
            return;
        }

        try
        {
            var wasPaused = trackingService.State.IsPaused;
            await trackingService.PauseAsync(CancellationToken.None);
            var result = await backupService.ImportAsync(path, CancellationToken.None);
            if (!wasPaused)
            {
                await trackingService.ResumeAsync(CancellationToken.None);
            }

            StatusMessage = $"Import abgeschlossen: {result.GameCount} Spiele, {result.SessionCount} Sessions";
        }
        catch (YFTimeTrackerException ex)
        {
            StatusMessage = ex.Message;
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
}
