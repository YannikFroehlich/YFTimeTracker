using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.Views;

public sealed partial class SessionsPage : Page
{
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly IGameTrackingService trackingService;
    private string runningGamesSignature = string.Empty;
    private long? requestedSessionId;

    public SessionsPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<SessionsViewModel>();
        trackingService = App.Services.GetRequiredService<IGameTrackingService>();
        liveTimer.Tick += LiveTimer_Tick;
    }

    private SessionsViewModel ViewModel => (SessionsViewModel)DataContext;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        requestedSessionId = e.Parameter is long sessionId ? sessionId : null;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Sessions.CollectionChanged -= Sessions_CollectionChanged;
        ViewModel.Sessions.CollectionChanged += Sessions_CollectionChanged;
        trackingService.StateChanged -= TrackingService_StateChanged;
        trackingService.StateChanged += TrackingService_StateChanged;
        if (requestedSessionId is { } sessionId)
        {
            requestedSessionId = null;
            await ViewModel.ShowSessionAsync(sessionId);
            if (ViewModel.SelectedSession is not null)
            {
                SessionsList.ScrollIntoView(ViewModel.SelectedSession);
            }
        }
        else
        {
            await ViewModel.RefreshAsync();
        }
        runningGamesSignature = CreateRunningGamesSignature(trackingService.State);
        UpdateEmptyState();
        UpdateResponsiveLayout(SessionsRoot.ActualWidth);
        liveTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        liveTimer.Stop();
        ViewModel.Sessions.CollectionChanged -= Sessions_CollectionChanged;
        trackingService.StateChanged -= TrackingService_StateChanged;
    }

    private void LiveTimer_Tick(object? sender, object e)
    {
        ViewModel.RefreshLiveDurations();
    }

    private void Sessions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
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

    private void UpdateEmptyState()
    {
        if (EmptyState is null || SessionsList is null)
        {
            return;
        }

        EmptyState.Visibility = ViewModel.Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionsList.Visibility = ViewModel.Sessions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        var session = ViewModel.SelectedSession;
        if (session is null || !session.CanModify || SessionsRoot.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = SessionsRoot.XamlRoot,
            Title = "Session löschen?",
            Content = new TextBlock
            {
                MaxWidth = 430,
                Text = $"Die Session von {session.GameName} am {session.StartedAt} wird dauerhaft gelöscht.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSelectedAsync();
        }
    }

    private void SessionsRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var compactHeader = width < 640;
        Grid.SetRow(HeaderActions, compactHeader ? 1 : 0);
        Grid.SetColumn(HeaderActions, compactHeader ? 0 : 1);
        HeaderActions.HorizontalAlignment = compactHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        HeaderActions.Margin = compactHeader ? new Thickness(0, 12, 0, 0) : new Thickness(0);

        var stackSummary = width < 760;
        SummaryGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        SummaryGrid.ColumnDefinitions[1].Width = stackSummary ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SummaryGrid.ColumnDefinitions[2].Width = stackSummary ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        PositionSummaryCard(CountCard, 0, 0);
        PositionSummaryCard(TotalCard, stackSummary ? 1 : 0, stackSummary ? 0 : 1);
        PositionSummaryCard(AverageCard, stackSummary ? 2 : 0, stackSummary ? 0 : 2);

        var stackFilters = width < 900;
        FilterGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        FilterGrid.ColumnDefinitions[1].Width = stackFilters ? new GridLength(1, GridUnitType.Star) : new GridLength(220);
        FilterGrid.ColumnDefinitions[2].Width = stackFilters ? new GridLength(0) : new GridLength(200);
        FilterGrid.ColumnDefinitions[3].Width = stackFilters ? new GridLength(0) : GridLength.Auto;
        PositionFilter(SearchBox, 0, 0, stackFilters ? 2 : 1);
        PositionFilter(GameFilterBox, stackFilters ? 1 : 0, stackFilters ? 0 : 1);
        PositionFilter(PeriodFilterBox, stackFilters ? 1 : 0, stackFilters ? 1 : 2);
        PositionFilter(FilterButtons, stackFilters ? 2 : 0, stackFilters ? 0 : 3, stackFilters ? 2 : 1);

        var stackContent = width < 1080;
        SessionContent.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        SessionContent.ColumnDefinitions[1].Width = stackContent ? new GridLength(0) : new GridLength(390);
        Grid.SetColumn(TimelineCard, 0);
        Grid.SetRow(TimelineCard, 0);
        Grid.SetColumn(EditorCard, stackContent ? 0 : 1);
        Grid.SetRow(EditorCard, stackContent ? 1 : 0);
    }

    private static void PositionSummaryCard(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }

    private static void PositionFilter(FrameworkElement element, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }
}
