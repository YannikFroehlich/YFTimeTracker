using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Windows.Graphics;
using Windows.UI;
using YFTimeTracker.App.Services;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.App.Views;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App;

public sealed partial class MainWindow : Window
{
    private readonly DashboardViewModel dashboardViewModel;
    private readonly DispatcherTimer dashboardRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly ISettingsStore settingsStore;
    private readonly IAppUpdateService appUpdateService;
    private readonly IFirstRunSetupService firstRunSetupService;
    private readonly IGameTrackingService trackingService;
    private readonly AppWindow appWindow;
    private readonly SemaphoreSlim dialogLock = new(1, 1);
    private bool minimizeOnClose = true;
    private bool firstRunSetupActive;

    public MainWindow()
    {
        InitializeComponent();
        dashboardViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        settingsStore = App.Services.GetRequiredService<ISettingsStore>();
        appUpdateService = App.Services.GetRequiredService<IAppUpdateService>();
        firstRunSetupService = App.Services.GetRequiredService<IFirstRunSetupService>();
        trackingService = App.Services.GetRequiredService<IGameTrackingService>();
        RootGrid.DataContext = dashboardViewModel;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        appWindow = ConfigureWindow();
        appWindow.Closing += AppWindow_Closing;

        Navigation.SelectedItem = Navigation.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));

        dashboardRefreshTimer.Tick += DashboardRefreshTimer_Tick;
        dashboardRefreshTimer.Start();
        RootGrid.Loaded += async (_, _) =>
        {
            minimizeOnClose = await settingsStore.GetBoolAsync(AppSettingKeys.MinimizeOnClose, true, CancellationToken.None);
            await dashboardViewModel.RefreshAsync();
        };
    }

    public void ShowDashboard()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Navigation.SelectedItem = Navigation.MenuItems[0];
            Navigate(typeof(DashboardPage));
            appWindow.Show();
            Activate();
            if (!dashboardRefreshTimer.IsEnabled)
            {
                dashboardRefreshTimer.Start();
            }
        });
    }

    public void ShowLibrary()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Navigation.SelectedItem = Navigation.MenuItems[1];
            Navigate(typeof(GamesPage));
            appWindow.Show();
            Activate();
        });
    }

    public void ShowGameDetails(long gameId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Navigation.SelectedItem = Navigation.MenuItems[1];
            ContentFrame.Navigate(typeof(GameDetailsPage), gameId);
            appWindow.Show();
            Activate();
        });
    }

    public void HideToTray()
    {
        appWindow.Hide();
        dashboardRefreshTimer.Stop();
    }

    public void SetMinimizeOnClose(bool value)
    {
        minimizeOnClose = value;
    }

    public async Task<bool> ShowFirstRunSetupIfRequiredAsync(bool force = false)
    {
        if (!force && await firstRunSetupService.IsCompletedAsync(CancellationToken.None))
        {
            return false;
        }

        if (!await dialogLock.WaitAsync(0))
        {
            return false;
        }

        try
        {
            await WaitForRootGridAsync();
            var options = await firstRunSetupService.LoadOptionsAsync(CancellationToken.None);
            var dialog = new FirstRunSetupDialog(firstRunSetupService, options)
            {
                XamlRoot = RootGrid.XamlRoot
            };

            firstRunSetupActive = true;
            await dialog.ShowAsync();
            if (dialog.WasCompleted)
            {
                var selectedOptions = dialog.SelectedOptions;
                minimizeOnClose = selectedOptions.MinimizeOnClose;
                if (trackingService.State.IsRunning)
                {
                    if (selectedOptions.TrackingEnabled && trackingService.State.IsPaused)
                    {
                        await trackingService.ResumeAsync(CancellationToken.None);
                    }
                    else if (!selectedOptions.TrackingEnabled && !trackingService.State.IsPaused)
                    {
                        await trackingService.PauseAsync(CancellationToken.None);
                    }
                }
            }

            return true;
        }
        finally
        {
            firstRunSetupActive = false;
            dialogLock.Release();
        }
    }

    public async Task CheckForUpdatesOnStartupAsync(bool showPrompt)
    {
        try
        {
            var state = appUpdateService.State.Stage == AppUpdateStage.ReadyToInstall
                ? appUpdateService.State
                : await appUpdateService.CheckForUpdatesAsync(CancellationToken.None);

            if (showPrompt && state.HasAvailableUpdate)
            {
                await PromptForAvailableUpdateAsync();
            }
        }
        catch (Exception)
        {
            // Update errors are exposed in the settings and must never interrupt app startup.
        }
    }

    public async Task CheckForUpdatesManuallyAsync()
    {
        var state = appUpdateService.State.Stage == AppUpdateStage.ReadyToInstall
            ? appUpdateService.State
            : await appUpdateService.CheckForUpdatesAsync(CancellationToken.None);

        if (state.HasAvailableUpdate)
        {
            await PromptForAvailableUpdateAsync();
            return;
        }

        await ShowUpdateMessageAsync("App-Updates", state.Message);
    }

    public async Task PromptForAvailableUpdateAsync()
    {
        if (!await dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var state = appUpdateService.State;
            if (!state.HasAvailableUpdate || RootGrid.XamlRoot is null)
            {
                return;
            }

            var isReady = state.Stage == AppUpdateStage.ReadyToInstall;
            var version = state.AvailableVersion ?? "neu";
            var sizeText = FormatDownloadSize(state.DownloadSize);
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = $"YFTimeTracker {version} ist verfügbar",
                Content = new TextBlock
                {
                    MaxWidth = 470,
                    Text = isReady
                        ? "Das Update wurde bereits heruntergeladen. YFTimeTracker beendet offene Sessions sauber und startet nach der Installation neu."
                        : $"Das Update{sizeText} wird aus dem öffentlichen GitHub-Release geladen. Danach beendet YFTimeTracker offene Sessions sauber und startet mit der neuen Version neu.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = isReady ? "Neu starten & installieren" : "Herunterladen & installieren",
                CloseButtonText = "Später",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (!isReady && !await DownloadUpdateWithProgressAsync())
            {
                return;
            }

            try
            {
                appUpdateService.ScheduleInstallAndRestart();
                await App.ShutdownAsync();
            }
            catch (Exception)
            {
                await ShowUpdateMessageCoreAsync(
                    "Update konnte nicht gestartet werden",
                    "Die Installation konnte nicht vorbereitet werden. Bitte YFTimeTracker neu starten und erneut versuchen.");
            }
        }
        finally
        {
            dialogLock.Release();
        }
    }

    private async void DashboardRefreshTimer_Tick(object? sender, object e)
    {
        await dashboardViewModel.RefreshAsync();
    }

    private async Task<bool> DownloadUpdateWithProgressAsync()
    {
        if (RootGrid.XamlRoot is null)
        {
            return false;
        }

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 6
        };
        var progressText = new TextBlock
        {
            Text = "Download wird vorbereitet …",
            TextWrapping = TextWrapping.Wrap
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(progressText);
        content.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Update wird heruntergeladen",
            Content = content
        };

        _ = progressDialog.ShowAsync();
        await Task.Yield();

        var progress = new Progress<int>(value =>
        {
            progressBar.Value = value;
            progressText.Text = $"Neue Version wird sicher heruntergeladen · {value} %";
        });

        try
        {
            await appUpdateService.DownloadUpdateAsync(progress, CancellationToken.None);
            progressDialog.Hide();
            return true;
        }
        catch (Exception)
        {
            progressDialog.Hide();
            await Task.Delay(150);
            await ShowUpdateMessageCoreAsync("Download fehlgeschlagen", appUpdateService.State.Message);
            return false;
        }
    }

    private async Task ShowUpdateMessageAsync(string title, string message)
    {
        if (!await dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            await ShowUpdateMessageCoreAsync(title, message);
        }
        finally
        {
            dialogLock.Release();
        }
    }

    private async Task ShowUpdateMessageCoreAsync(string title, string message)
    {
        if (RootGrid.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                MaxWidth = 470,
                Text = message,
                TextWrapping = TextWrapping.Wrap
            },
            CloseButtonText = "Schließen"
        };
        await dialog.ShowAsync();
    }

    private static string FormatDownloadSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        return bytes >= 1024L * 1024L
            ? $" mit {bytes / (1024d * 1024d):0.#} MB"
            : $" mit {bytes / 1024d:0.#} KB";
    }

    private Task WaitForRootGridAsync()
    {
        if (RootGrid.XamlRoot is not null)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? loadedHandler = null;
        loadedHandler = (_, _) =>
        {
            RootGrid.Loaded -= loadedHandler;
            completion.TrySetResult(true);
        };
        RootGrid.Loaded += loadedHandler;
        return completion.Task;
    }

    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is not string tag)
        {
            return;
        }

        var pageType = tag switch
        {
            "library" => typeof(GamesPage),
            "sessions" => typeof(SessionsPage),
            "statistics" => typeof(StatisticsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(DashboardPage)
        };

        Navigate(pageType);
    }

    private void Navigate(Type pageType)
    {
        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.IsShuttingDown)
        {
            return;
        }

        args.Cancel = true;
        if (firstRunSetupActive)
        {
            appWindow.Show();
            Activate();
            return;
        }

        if (minimizeOnClose)
        {
            HideToTray();
            return;
        }

        await App.ShutdownAsync();
    }

    private AppWindow ConfigureWindow()
    {
        var windowHandle = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var width = Math.Min(1500, Math.Max(1080, displayArea.WorkArea.Width - 80));
        var height = Math.Min(940, Math.Max(720, displayArea.WorkArea.Height - 80));
        var x = displayArea.WorkArea.X + (displayArea.WorkArea.Width - width) / 2;
        var y = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - height) / 2;

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        appWindow.Title = "YFTimeTracker";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "YFTimeTracker.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = Color.FromArgb(255, 244, 247, 255);
        appWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 19, 39, 66);
        appWindow.TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 57, 92);
        return appWindow;
    }
}
