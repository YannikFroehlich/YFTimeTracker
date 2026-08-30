using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YFTimeTracker.App.ViewModels;

namespace YFTimeTracker.App.Views;

public sealed partial class DashboardPage : Page
{
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public DashboardPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<DashboardViewModel>();
        liveTimer.Tick += (_, _) => ViewModel.Tick();
    }

    private DashboardViewModel ViewModel => (DashboardViewModel)DataContext;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
        ViewModel.Tick();
        liveTimer.Start();
        UpdateLayout(DashboardRoot.ActualWidth);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        liveTimer.Stop();
    }

    private void DashboardRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayout(e.NewSize.Width);
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.ShowLibrary();
    }

    private void UpdateLayout(double width)
    {
        var compact = width < 980;

        StatsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        StatsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        StatsGrid.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        Grid.SetColumn(TodayCard, 0);
        Grid.SetColumn(WeekCard, compact ? 0 : 1);
        Grid.SetColumn(TotalCard, compact ? 0 : 2);
        Grid.SetRow(TodayCard, 0);
        Grid.SetRow(WeekCard, compact ? 1 : 0);
        Grid.SetRow(TotalCard, compact ? 2 : 0);

        TodayCard.Margin = compact ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 8, 0);
        WeekCard.Margin = compact ? new Thickness(0, 0, 0, 10) : new Thickness(6, 0, 6, 0);
        TotalCard.Margin = compact ? new Thickness(0) : new Thickness(8, 0, 0, 0);

        DetailsGrid.ColumnDefinitions[0].Width = compact
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0.92, GridUnitType.Star);
        DetailsGrid.ColumnDefinitions[1].Width = compact
            ? new GridLength(0)
            : new GridLength(1.08, GridUnitType.Star);

        Grid.SetColumn(TrackingCard, 0);
        Grid.SetColumn(ChartCard, compact ? 0 : 1);
        Grid.SetRow(TrackingCard, 0);
        Grid.SetRow(ChartCard, compact ? 1 : 0);
        TrackingCard.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0);
        ChartCard.Margin = compact ? new Thickness(0) : new Thickness(14, 0, 0, 0);

        ActiveGameContent.ColumnDefinitions[0].Width = new GridLength(compact ? 150 : 180);
    }
}
