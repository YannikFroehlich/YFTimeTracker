using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YFTimeTracker.App.ViewModels;

namespace YFTimeTracker.App.Views;

public sealed partial class GamesPage : Page
{
    public GamesPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<GamesViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ((GamesViewModel)DataContext).RefreshAsync();
        UpdateLayout(LibraryRoot.ActualWidth);
    }

    private void LibraryRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayout(e.NewSize.Width);
    }

    private void UpdateLayout(double width)
    {
        var compact = width < 980;
        LibraryContent.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(360);
        LibraryContent.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        Grid.SetColumn(GamesListCard, 0);
        Grid.SetRow(GamesListCard, 0);
        Grid.SetColumn(EditorCard, compact ? 0 : 1);
        Grid.SetRow(EditorCard, compact ? 1 : 0);
        GamesListCard.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0);
        EditorCard.Margin = compact ? new Thickness(0) : new Thickness(14, 0, 0, 0);
    }
}
