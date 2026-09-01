using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using YFTimeTracker.App.Services;
using YFTimeTracker.App.ViewModels;

namespace YFTimeTracker.App.Views;

public sealed partial class YearReviewPage : Page
{
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public YearReviewPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<YearReviewViewModel>();
        refreshTimer.Tick += RefreshTimer_Tick;
    }

    private YearReviewViewModel ViewModel => (YearReviewViewModel)DataContext;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        UpdateResponsiveLayout(ReviewRoot.ActualWidth);
        refreshTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        refreshTimer.Stop();
    }

    private async void RefreshTimer_Tick(object? sender, object e)
    {
        await ViewModel.RefreshAsync();
    }

    private async void ShareButton_Click(object sender, RoutedEventArgs e)
    {
        var filePicker = App.Services.GetRequiredService<IFilePickerService>();
        var year = ViewModel.SelectedYear?.Year ?? DateTime.Now.Year;
        var path = await filePicker.PickYearReviewImageAsync(year, CancellationToken.None);
        if (path is null)
        {
            return;
        }

        try
        {
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(ShareContent);
            var pixels = await bitmap.GetPixelsAsync();

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,
                (uint)bitmap.PixelHeight,
                96,
                96,
                pixels.ToArray());
            await encoder.FlushAsync();

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            await File.WriteAllBytesAsync(path, bytes);
            ViewModel.StatusMessage = $"Jahresrückblick als Bild gespeichert: {Path.GetFileName(path)}";
            ViewModel.SetExportedFile(path);
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = $"Bild konnte nicht erstellt werden: {exception.Message}";
        }
    }

    private void TopGames_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is YearReviewGameItemViewModel game)
        {
            App.MainWindow?.ShowGameDetails(game.GameId);
        }
    }

    private void ReviewRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var compactHeader = width < 700;
        Grid.SetRow(HeaderActions, compactHeader ? 1 : 0);
        Grid.SetColumn(HeaderActions, compactHeader ? 0 : 1);
        HeaderActions.HorizontalAlignment = compactHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        HeaderActions.Margin = compactHeader ? new Thickness(0, 12, 0, 0) : new Thickness(0);

        var summaryColumns = width >= 900 ? 3 : width >= 620 ? 2 : 1;
        PositionSummaryCards(summaryColumns);

        var stackContent = width < 980;
        ReviewContentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        ReviewContentGrid.ColumnDefinitions[1].Width = stackContent ? new GridLength(0) : new GridLength(0.65, GridUnitType.Star);
        Grid.SetRow(MonthCard, 0);
        Grid.SetColumn(MonthCard, 0);
        Grid.SetRow(HighlightsCard, stackContent ? 1 : 0);
        Grid.SetColumn(HighlightsCard, stackContent ? 0 : 1);
    }

    private void PositionSummaryCards(int columns)
    {
        for (var index = 0; index < SummaryGrid.ColumnDefinitions.Count; index++)
        {
            SummaryGrid.ColumnDefinitions[index].Width = index < columns
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);
        }

        Position(ActiveDaysCard, 0, 0);
        Position(GamesCard, columns == 1 ? 1 : 0, columns == 1 ? 0 : 1);
        Position(SessionsCard, columns == 3 ? 0 : columns == 2 ? 1 : 2, columns == 3 ? 2 : 0);
    }

    private static void Position(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }
}
