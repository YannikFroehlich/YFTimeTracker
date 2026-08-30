using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.Views;

public sealed partial class GamesPage : Page
{
    private readonly IGameTrackingService trackingService;
    private string runningGamesSignature = string.Empty;

    public GamesPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<GamesViewModel>();
        trackingService = App.Services.GetRequiredService<IGameTrackingService>();
    }

    private GamesViewModel ViewModel => (GamesViewModel)DataContext;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        trackingService.StateChanged -= TrackingService_StateChanged;
        trackingService.StateChanged += TrackingService_StateChanged;
        await ViewModel.RefreshAsync();
        runningGamesSignature = CreateRunningGamesSignature(trackingService.State);
        UpdateLayout(LibraryRoot.ActualWidth);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        trackingService.StateChanged -= TrackingService_StateChanged;
    }

    private void LibraryRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayout(e.NewSize.Width);
    }

    private void OpenGameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: long gameId })
        {
            App.MainWindow?.ShowGameDetails(gameId);
        }
    }

    private void UpdateLayout(double width)
    {
        var compactFilters = width < 1080;
        var narrowFilters = width < 650;
        LibraryFilterGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        LibraryFilterGrid.ColumnDefinitions[1].Width = narrowFilters
            ? new GridLength(0)
            : compactFilters ? new GridLength(1, GridUnitType.Star) : new GridLength(175);
        LibraryFilterGrid.ColumnDefinitions[2].Width = compactFilters ? new GridLength(0) : new GridLength(175);
        LibraryFilterGrid.ColumnDefinitions[3].Width = compactFilters ? new GridLength(0) : new GridLength(190);
        LibraryFilterGrid.ColumnDefinitions[4].Width = compactFilters ? new GridLength(0) : GridLength.Auto;
        PositionFilter(LibrarySearchBox, 0, 0, compactFilters && !narrowFilters ? 2 : 1);
        PositionFilter(SourceFilterBox, compactFilters ? 1 : 0, compactFilters ? 0 : 1);
        PositionFilter(StatusFilterBox, narrowFilters ? 2 : compactFilters ? 1 : 0, narrowFilters ? 0 : compactFilters ? 1 : 2);
        PositionFilter(SortBox, narrowFilters ? 3 : compactFilters ? 2 : 0, compactFilters ? 0 : 3);
        PositionFilter(ClearFiltersButton, narrowFilters ? 4 : compactFilters ? 2 : 0, narrowFilters ? 0 : compactFilters ? 1 : 4);

        var compact = width < 980;
        LibraryContent.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(430);
        LibraryContent.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        Grid.SetColumn(GamesListCard, 0);
        Grid.SetRow(GamesListCard, 0);
        Grid.SetColumn(EditorCard, compact ? 0 : 1);
        Grid.SetRow(EditorCard, compact ? 1 : 0);
        GamesListCard.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0);
        EditorCard.Margin = compact ? new Thickness(0) : new Thickness(14, 0, 0, 0);
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

    private static void PositionFilter(FrameworkElement element, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }
}
