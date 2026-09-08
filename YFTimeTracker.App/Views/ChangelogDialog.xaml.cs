using Microsoft.UI.Xaml.Controls;

namespace YFTimeTracker.App.Views;

public sealed partial class ChangelogDialog : ContentDialog
{
    public ChangelogDialog(string heading, IReadOnlyList<string> bullets)
    {
        InitializeComponent();
        VersionText.Text = heading;

        foreach (var row in BulletList.BuildRows(bullets))
        {
            BulletsPanel.Children.Add(row);
        }
    }
}
