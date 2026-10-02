using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YFTimeTracker.App.ViewModels;
using static YFTimeTracker.App.Views.GridDefinitions;

namespace YFTimeTracker.App.Views;

public sealed partial class GameDetailsPage : Page
{
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private long gameId;

    public GameDetailsPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<GameDetailsViewModel>();
        liveTimer.Tick += (_, _) => ViewModel.RefreshLiveDurations();
    }

    private GameDetailsViewModel ViewModel => (GameDetailsViewModel)DataContext;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        gameId = e.Parameter is long id ? id : 0;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync(gameId);
        UpdateResponsiveLayout(DetailsRoot.ActualWidth);
        liveTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        liveTimer.Stop();
    }

    private void BackToLibrary_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.ShowLibrary();
    }

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        var session = ViewModel.SelectedSession;
        if (session is null || !session.CanModify || DetailsRoot.XamlRoot is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = DetailsRoot.XamlRoot,
            Title = "Session löschen?",
            Content = new TextBlock
            {
                MaxWidth = 430,
                Text = $"Die Session am {session.StartedAt} wird dauerhaft gelöscht.",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "Löschen",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteSelectedSessionAsync();
        }
    }

    private void DetailsRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        CoverActions.Orientation = width < 620 ? Orientation.Vertical : Orientation.Horizontal;
        var compactHero = width < 900;
        if (compactHero)
        {
            SetColumns(HeroGrid, new GridLength(94), Star());
        }
        else
        {
            SetColumns(HeroGrid, new GridLength(94), Star(), new GridLength(330));
        }

        SetRows(HeroGrid, compactHero ? 2 : 1);
        Grid.SetRow(NameEditor, compactHero ? 1 : 0);
        Grid.SetColumn(NameEditor, compactHero ? 0 : 2);
        Grid.SetColumnSpan(NameEditor, compactHero ? 3 : 1);

        var stackSummary = width < 720;
        var twoColumnSummary = !stackSummary && width < 1080;
        var summaryColumns = stackSummary ? 1 : twoColumnSummary ? 2 : 4;
        SetColumns(SummaryGrid, Enumerable.Repeat(Star(), summaryColumns).ToArray());
        SetRows(SummaryGrid, 4 / summaryColumns);

        PositionSummaryCard(TotalCard, 0, 0);
        PositionSummaryCard(SessionCountCard, stackSummary ? 1 : 0, stackSummary ? 0 : 1);
        PositionSummaryCard(AverageCard, stackSummary ? 2 : twoColumnSummary ? 1 : 0, stackSummary ? 0 : twoColumnSummary ? 0 : 2);
        PositionSummaryCard(LastPlayedCard, stackSummary ? 3 : twoColumnSummary ? 1 : 0, stackSummary ? 0 : twoColumnSummary ? 1 : 3);

        var stackContent = width < 1050;
        if (stackContent)
        {
            SetColumns(ContentGrid, Star());
        }
        else
        {
            SetColumns(ContentGrid, Star(), new GridLength(380));
        }

        SetRows(ContentGrid, stackContent ? 2 : 1);
        Grid.SetColumn(MainColumn, 0);
        Grid.SetRow(MainColumn, 0);
        Grid.SetColumn(SideColumn, stackContent ? 0 : 1);
        Grid.SetRow(SideColumn, stackContent ? 1 : 0);
    }

    private static void PositionSummaryCard(FrameworkElement card, int row, int column)
    {
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
    }
}
