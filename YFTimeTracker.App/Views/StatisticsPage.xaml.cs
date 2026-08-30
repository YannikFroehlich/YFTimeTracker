using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.Views;

public sealed partial class StatisticsPage : Page
{
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly IGameTrackingService trackingService;
    private string runningGamesSignature = string.Empty;

    public StatisticsPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<StatisticsViewModel>();
        trackingService = App.Services.GetRequiredService<IGameTrackingService>();
        refreshTimer.Tick += RefreshTimer_Tick;
    }

    private StatisticsViewModel ViewModel => (StatisticsViewModel)DataContext;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        trackingService.StateChanged -= TrackingService_StateChanged;
        trackingService.StateChanged += TrackingService_StateChanged;
        runningGamesSignature = CreateRunningGamesSignature(trackingService.State);
        await ViewModel.RefreshAsync();
        UpdateResponsiveLayout(StatisticsRoot.ActualWidth);
        refreshTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Stop();
        trackingService.StateChanged -= TrackingService_StateChanged;
    }

    private async void RefreshTimer_Tick(object? sender, object e)
    {
        await ViewModel.RefreshAsync();
    }

    private void TrackingService_StateChanged(object? sender, TrackingState state)
    {
        var signature = CreateRunningGamesSignature(state);
        if (string.Equals(signature, runningGamesSignature, StringComparison.Ordinal))
        {
            return;
        }

        runningGamesSignature = signature;
        DispatcherQueue.TryEnqueue(async () => await ViewModel.RefreshAsync());
    }

    private static string CreateRunningGamesSignature(TrackingState state)
    {
        return string.Join(",", state.RunningGames.Select(game => game.GameId).OrderBy(id => id));
    }

    private void StatisticsRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var compactHeader = width < 720;
        Grid.SetRow(HeaderActions, compactHeader ? 1 : 0);
        Grid.SetColumn(HeaderActions, compactHeader ? 0 : 1);
        HeaderActions.HorizontalAlignment = compactHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        HeaderActions.Margin = compactHeader ? new Thickness(0, 12, 0, 0) : new Thickness(0);

        PositionSummaryCards(width);

        var stackMain = width < 1050;
        MainContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        MainContentGrid.ColumnDefinitions[1].Width = stackMain ? new GridLength(0) : new GridLength(0.65, GridUnitType.Star);
        Grid.SetRow(TrendCard, 0);
        Grid.SetColumn(TrendCard, 0);
        Grid.SetRow(TopGamesCard, stackMain ? 1 : 0);
        Grid.SetColumn(TopGamesCard, stackMain ? 0 : 1);

        var stackLower = width < 980;
        LowerContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        LowerContentGrid.ColumnDefinitions[1].Width = stackLower ? new GridLength(0) : new GridLength(0.95, GridUnitType.Star);
        Grid.SetRow(WeekdayCard, 0);
        Grid.SetColumn(WeekdayCard, 0);
        Grid.SetRow(InsightsCard, stackLower ? 1 : 0);
        Grid.SetColumn(InsightsCard, stackLower ? 0 : 1);

        var stackInsights = width < 640;
        InsightsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        InsightsGrid.ColumnDefinitions[1].Width = stackInsights ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        InsightsGrid.ColumnDefinitions[2].Width = stackInsights ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(TopGameInsight, 0);
        Grid.SetColumn(TopGameInsight, 0);
        Grid.SetRow(WeekdayInsight, stackInsights ? 1 : 0);
        Grid.SetColumn(WeekdayInsight, stackInsights ? 0 : 1);
        Grid.SetRow(LongestInsight, stackInsights ? 2 : 0);
        Grid.SetColumn(LongestInsight, stackInsights ? 0 : 2);
        WeekdayInsight.Margin = stackInsights ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        LongestInsight.Margin = stackInsights ? new Thickness(0, 10, 0, 0) : new Thickness(0);
    }

    private void PositionSummaryCards(double width)
    {
        if (width >= 1100)
        {
            SetSummaryColumns(4);
            Position(TotalCard, 0, 0);
            Position(SessionCard, 0, 1);
            Position(AverageCard, 0, 2);
            Position(GamesCard, 0, 3);
            return;
        }

        if (width >= 650)
        {
            SetSummaryColumns(2);
            Position(TotalCard, 0, 0);
            Position(SessionCard, 0, 1);
            Position(AverageCard, 1, 0);
            Position(GamesCard, 1, 1);
            return;
        }

        SetSummaryColumns(1);
        Position(TotalCard, 0, 0);
        Position(SessionCard, 1, 0);
        Position(AverageCard, 2, 0);
        Position(GamesCard, 3, 0);
    }

    private void SetSummaryColumns(int visibleColumns)
    {
        for (var index = 0; index < SummaryGrid.ColumnDefinitions.Count; index++)
        {
            SummaryGrid.ColumnDefinitions[index].Width = index < visibleColumns
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }
    }

    private static void Position(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }
}
