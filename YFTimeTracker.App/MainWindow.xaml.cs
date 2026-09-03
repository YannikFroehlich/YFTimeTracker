using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;
using WinRT.Interop;
using Windows.Graphics;
using Windows.UI;
using YFTimeTracker.App.Services;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.App.Views;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.App;

public sealed partial class MainWindow : Window
{
    private readonly DashboardViewModel dashboardViewModel;
    private readonly DispatcherTimer dashboardRefreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly ISettingsStore settingsStore;
    private readonly IAppUpdateService appUpdateService;
    private readonly INotificationLogRepository notificationLog;
    private readonly IFirstRunSetupService firstRunSetupService;
    private readonly IGameTrackingService trackingService;
    private readonly IThemeService themeService;
    private readonly AppWindow appWindow;
    private readonly SemaphoreSlim dialogLock = new(1, 1);
    private CancellationTokenSource? globalSearchCancellation;
    private bool minimizeOnClose = true;
    private bool firstRunSetupActive;
    private bool changelogCheckStarted;
    private string? currentProfileDisplayName;
    private string? currentProfileAccentColor;
    private string? pendingProfileAccentColor;

    public MainWindow()
    {
        GlobalSearchViewModel = App.Services.GetRequiredService<GlobalSearchViewModel>();
        InitializeComponent();
        dashboardViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        settingsStore = App.Services.GetRequiredService<ISettingsStore>();
        appUpdateService = App.Services.GetRequiredService<IAppUpdateService>();
        notificationLog = App.Services.GetRequiredService<INotificationLogRepository>();
        firstRunSetupService = App.Services.GetRequiredService<IFirstRunSetupService>();
        trackingService = App.Services.GetRequiredService<IGameTrackingService>();
        themeService = App.Services.GetRequiredService<IThemeService>();
        RootGrid.DataContext = dashboardViewModel;
        RootGrid.RequestedTheme = themeService.CurrentTheme;
        themeService.ThemeChanged += ThemeService_ThemeChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        appWindow = ConfigureWindow();
        appWindow.Closing += AppWindow_Closing;
        RootGrid.ActualThemeChanged += (_, _) => ApplyTitleBarButtonColors();
        ApplyTitleBarButtonColors();

        Navigation.SelectedItem = Navigation.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));

        dashboardRefreshTimer.Tick += DashboardRefreshTimer_Tick;
        dashboardRefreshTimer.Start();
        RootGrid.Loaded += async (_, _) =>
        {
            minimizeOnClose = await settingsStore.GetBoolAsync(AppSettingKeys.MinimizeOnClose, true, CancellationToken.None);
            var profileName = await settingsStore.GetAsync(AppSettingKeys.ProfileDisplayName, CancellationToken.None);
            var profileAccentColor = await settingsStore.GetAsync(AppSettingKeys.ProfileAccentColor, CancellationToken.None);
            ApplyProfileHeader(profileName, profileAccentColor);
            await RefreshNotificationBadgeAsync();
            await dashboardViewModel.RefreshAsync();
        };
    }

    public GlobalSearchViewModel GlobalSearchViewModel { get; }

    public ObservableCollection<NotificationListItemViewModel> NotificationItems { get; } = [];

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

            _ = ShowChangelogIfAvailableAsync();
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

    public void ShowSession(long sessionId)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            Navigation.SelectedItem = Navigation.MenuItems[2];
            ContentFrame.Navigate(typeof(SessionsPage), sessionId);
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

    private void ThemeService_ThemeChanged(object? sender, ElementTheme theme)
    {
        DispatcherQueue.TryEnqueue(() => RootGrid.RequestedTheme = theme);
    }

    private void ApplyProfileHeader(string? displayName, string? accentColorHex)
    {
        currentProfileDisplayName = displayName;
        currentProfileAccentColor = accentColorHex;

        var trimmedName = displayName?.Trim();
        ProfileNameText.Text = string.IsNullOrWhiteSpace(trimmedName) ? "Lokales Profil" : trimmedName;
        ProfileInitialsText.Text = string.IsNullOrWhiteSpace(trimmedName) ? "YF" : GetInitials(trimmedName!);
        ProfileAvatarBorder.Background = string.IsNullOrWhiteSpace(accentColorHex)
            ? (Brush)Application.Current.Resources["YFLogoGradientBrush"]
            : new SolidColorBrush(ParseAccentColor(accentColorHex));
    }

    private void ProfileFlyout_Opening(object? sender, object e)
    {
        ProfileNameInput.Text = currentProfileDisplayName ?? string.Empty;
        pendingProfileAccentColor = currentProfileAccentColor;
        HighlightSelectedSwatch(pendingProfileAccentColor);
    }

    private void ProfileSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string accentColorHex })
        {
            return;
        }

        pendingProfileAccentColor = string.IsNullOrEmpty(accentColorHex) ? null : accentColorHex;
        HighlightSelectedSwatch(pendingProfileAccentColor);
    }

    private void HighlightSelectedSwatch(string? accentColorHex)
    {
        foreach (var child in ProfileSwatchPanel.Children)
        {
            if (child is not Button { Tag: string swatchColorHex } swatch)
            {
                continue;
            }

            var isSelected = string.Equals(swatchColorHex, accentColorHex ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = isSelected
                ? (Brush)Application.Current.Resources["YFTextBrush"]
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    private async void ProfileSaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameInput.Text.Trim();
        await settingsStore.SetAsync(AppSettingKeys.ProfileDisplayName, name, CancellationToken.None);
        await settingsStore.SetAsync(AppSettingKeys.ProfileAccentColor, pendingProfileAccentColor ?? string.Empty, CancellationToken.None);
        ApplyProfileHeader(name, pendingProfileAccentColor);
        ProfileFlyout.Hide();
    }

    private static string GetInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private static Color ParseAccentColor(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        if (value.Length == 6)
        {
            value = "FF" + value;
        }

        if (value.Length == 8 && uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        return Color.FromArgb(255, 49, 130, 255);
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

    public async Task ShowChangelogIfAvailableAsync(bool silent = false)
    {
        if (changelogCheckStarted)
        {
            return;
        }

        changelogCheckStarted = true;

        var latest = ReadLatestChangelogEntry();
        if (latest is null || latest.Bullets.Count == 0)
        {
            return;
        }

        var lastSeenHeading = await settingsStore.GetAsync(AppSettingKeys.LastSeenChangelogHeading, CancellationToken.None);
        if (!silent
            && !string.Equals(latest.Heading, lastSeenHeading, StringComparison.Ordinal)
            && await dialogLock.WaitAsync(0))
        {
            try
            {
                await WaitForRootGridAsync();
                if (RootGrid.XamlRoot is not null)
                {
                    var dialog = new ChangelogDialog(latest.Heading, latest.Bullets)
                    {
                        XamlRoot = RootGrid.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
            finally
            {
                dialogLock.Release();
            }
        }

        await settingsStore.SetAsync(AppSettingKeys.LastSeenChangelogHeading, latest.Heading, CancellationToken.None);
    }

    private static ChangelogEntry? ReadLatestChangelogEntry()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("CHANGELOG.md");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return ChangelogParser.TryGetLatestEntry(reader.ReadToEnd());
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            if (appUpdateService.State.Stage != AppUpdateStage.ReadyToInstall)
            {
                await appUpdateService.CheckForUpdatesAsync(CancellationToken.None);
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
        await RefreshNotificationBadgeAsync();
    }

    private async Task RefreshNotificationBadgeAsync()
    {
        var unreadCount = await notificationLog.GetUnreadCountAsync(CancellationToken.None);
        NotificationBadge.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void NotificationsFlyout_Opening(object? sender, object e)
    {
        var entries = await notificationLog.GetRecentAsync(20, CancellationToken.None);
        NotificationItems.Clear();
        foreach (var entry in entries)
        {
            var timestamp = TimeZoneInfo.ConvertTime(entry.CreatedAtUtc, TimeZoneInfo.Local);
            NotificationItems.Add(new NotificationListItemViewModel
            {
                Kind = entry.Kind,
                Title = entry.Title,
                Message = entry.Message,
                TimestampText = timestamp.ToString("dd.MM.yyyy · HH:mm"),
                RelatedGameId = entry.RelatedGameId
            });
        }

        NotificationsEmptyText.Visibility = NotificationItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        await notificationLog.MarkAllAsReadAsync(CancellationToken.None);
        NotificationBadge.Visibility = Visibility.Collapsed;
    }

    private async void NotificationsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not NotificationListItemViewModel item)
        {
            return;
        }

        NotificationsFlyout.Hide();

        if (item.Kind == NotificationKind.UpdateAvailable)
        {
            await PromptForAvailableUpdateAsync();
        }
        else if (item.RelatedGameId is { } gameId)
        {
            ShowGameDetails(gameId);
        }
    }

    private async void GlobalSearchBox_TextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        globalSearchCancellation?.Cancel();
        globalSearchCancellation?.Dispose();
        globalSearchCancellation = new CancellationTokenSource();
        var cancellationToken = globalSearchCancellation.Token;

        if (sender.Text.Trim().Length < 2)
        {
            GlobalSearchViewModel.Clear();
            sender.IsSuggestionListOpen = false;
            return;
        }

        try
        {
            await Task.Delay(180, cancellationToken);
            await GlobalSearchViewModel.SearchAsync(sender.Text, cancellationToken);
            sender.IsSuggestionListOpen = GlobalSearchViewModel.Results.Count > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            GlobalSearchViewModel.Clear();
            sender.IsSuggestionListOpen = false;
            Log.Warning(exception, "Global search failed.");
        }
    }

    private void GlobalSearchBox_SuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is GlobalSearchResultViewModel result)
        {
            sender.Text = result.Title;
        }
    }

    private void GlobalSearchBox_QuerySubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var result = args.ChosenSuggestion as GlobalSearchResultViewModel
            ?? GlobalSearchViewModel.Results.FirstOrDefault();
        if (result is not null)
        {
            OpenGlobalSearchResult(result);
        }
    }

    private void GlobalSearchResult_Tapped(
        object sender,
        Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: GlobalSearchResultViewModel result })
        {
            OpenGlobalSearchResult(result);
            args.Handled = true;
        }
    }

    private void OpenGlobalSearchResult(GlobalSearchResultViewModel result)
    {
        globalSearchCancellation?.Cancel();
        GlobalSearchBox.IsSuggestionListOpen = false;
        GlobalSearchBox.Text = string.Empty;
        GlobalSearchViewModel.Clear();

        switch (result.Kind)
        {
            case GlobalSearchResultKind.Game when result.GameId is { } gameId:
                ShowGameDetails(gameId);
                break;
            case GlobalSearchResultKind.Session when result.SessionId is { } sessionId:
                ShowSession(sessionId);
                break;
            case GlobalSearchResultKind.Library:
                Navigation.SelectedItem = Navigation.MenuItems[1];
                Navigate(typeof(GamesPage));
                break;
            case GlobalSearchResultKind.Sessions:
                Navigation.SelectedItem = Navigation.MenuItems[2];
                Navigate(typeof(SessionsPage));
                break;
            case GlobalSearchResultKind.Statistics:
                Navigation.SelectedItem = Navigation.MenuItems[3];
                Navigate(typeof(StatisticsPage));
                break;
            case GlobalSearchResultKind.YearReview:
                Navigation.SelectedItem = Navigation.MenuItems[4];
                Navigate(typeof(YearReviewPage));
                break;
        }
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
            "year-review" => typeof(YearReviewPage),
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
        return appWindow;
    }

    private void ApplyTitleBarButtonColors()
    {
        var isLight = RootGrid.ActualTheme == ElementTheme.Light;
        appWindow.TitleBar.ButtonForegroundColor = isLight
            ? Color.FromArgb(255, 17, 23, 38)
            : Color.FromArgb(255, 244, 247, 255);
        appWindow.TitleBar.ButtonHoverBackgroundColor = isLight
            ? Color.FromArgb(255, 228, 233, 245)
            : Color.FromArgb(255, 19, 39, 66);
        appWindow.TitleBar.ButtonPressedBackgroundColor = isLight
            ? Color.FromArgb(255, 207, 224, 255)
            : Color.FromArgb(255, 30, 57, 92);
    }
}
