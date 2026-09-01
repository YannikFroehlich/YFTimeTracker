using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace YFTimeTracker.App.Views;

public sealed partial class ChangelogDialog : ContentDialog
{
    public ChangelogDialog(string heading, IReadOnlyList<string> bullets)
    {
        InitializeComponent();
        VersionText.Text = heading;

        var bulletBrush = (Brush)Application.Current.Resources["YFBlueBrush"];
        foreach (var bullet in bullets)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bulletDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(0, 7, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Fill = bulletBrush
            };

            var text = new TextBlock { Text = bullet, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(text, 1);

            row.Children.Add(bulletDot);
            row.Children.Add(text);
            BulletsPanel.Children.Add(row);
        }
    }
}
