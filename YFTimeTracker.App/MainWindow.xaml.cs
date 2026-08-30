using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Windows.Graphics;
using Windows.UI;
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
    private readonly AppWindow appWindow;
    private bool minimizeOnClose = true;

    public MainWindow()
    {
        InitializeComponent();
        dashboardViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        settingsStore = App.Services.GetRequiredService<ISettingsStore>();
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

    public void HideToTray()
    {
        appWindow.Hide();
        dashboardRefreshTimer.Stop();
    }

    public void SetMinimizeOnClose(bool value)
    {
        minimizeOnClose = value;
    }

    private async void DashboardRefreshTimer_Tick(object? sender, object e)
    {
        await dashboardViewModel.RefreshAsync();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
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
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonForegroundColor = Color.FromArgb(255, 244, 247, 255);
        appWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 19, 39, 66);
        appWindow.TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 30, 57, 92);
        return appWindow;
    }
}
