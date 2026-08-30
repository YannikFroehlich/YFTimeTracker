using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YFTimeTracker.App.ViewModels;

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
        var compactHero = width < 900;
        HeroGrid.ColumnDefinitions[0].Width = new GridLength(94);
        HeroGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        HeroGrid.ColumnDefinitions[2].Width = compactHero ? new GridLength(0) : new GridLength(330);
        Grid.SetRow(NameEditor, compactHero ? 1 : 0);
        Grid.SetColumn(NameEditor, compactHero ? 0 : 2);
        Grid.SetColumnSpan(NameEditor, compactHero ? 3 : 1);

        var stackSummary = width < 720;
        var twoColumnSummary = !stackSummary && width < 1080;
        for (var index = 0; index < SummaryGrid.ColumnDefinitions.Count; index++)
        {
            SummaryGrid.ColumnDefinitions[index].Width = index < (stackSummary ? 1 : twoColumnSummary ? 2 : 4)
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }

        PositionSummaryCard(TotalCard, 0, 0);
        PositionSummaryCard(SessionCountCard, stackSummary ? 1 : 0, stackSummary ? 0 : 1);
        PositionSummaryCard(AverageCard, stackSummary ? 2 : twoColumnSummary ? 1 : 0, stackSummary ? 0 : twoColumnSummary ? 0 : 2);
        PositionSummaryCard(LastPlayedCard, stackSummary ? 3 : twoColumnSummary ? 1 : 0, stackSummary ? 0 : twoColumnSummary ? 1 : 3);

        var stackContent = width < 1050;
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        ContentGrid.ColumnDefinitions[1].Width = stackContent ? new GridLength(0) : new GridLength(380);
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
