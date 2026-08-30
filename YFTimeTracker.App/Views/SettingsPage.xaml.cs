using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YFTimeTracker.App.ViewModels;

namespace YFTimeTracker.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<SettingsViewModel>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ((SettingsViewModel)DataContext).LoadAsync();
        UpdateLayout(SettingsRoot.ActualWidth);
    }

    private void SettingsRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayout(e.NewSize.Width);
    }

    private void UpdateLayout(double width)
    {
        var compact = width < 900;
        SettingsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        SettingsGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        Grid.SetColumn(TrackingSettingsCard, 0);
        Grid.SetRow(TrackingSettingsCard, 0);
        Grid.SetColumn(WindowsSettingsCard, compact ? 0 : 1);
        Grid.SetRow(WindowsSettingsCard, compact ? 1 : 0);
        Grid.SetColumn(DataSettingsCard, 0);
        Grid.SetColumnSpan(DataSettingsCard, compact ? 1 : 2);
        Grid.SetRow(DataSettingsCard, compact ? 2 : 1);

        TrackingSettingsCard.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(0);
        WindowsSettingsCard.Margin = compact ? new Thickness(0, 0, 0, 12) : new Thickness(14, 0, 0, 0);
        DataSettingsCard.Margin = compact ? new Thickness(0) : new Thickness(0, 14, 0, 0);
    }
}
