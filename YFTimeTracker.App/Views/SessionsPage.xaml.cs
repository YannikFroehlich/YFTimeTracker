using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YFTimeTracker.App.Views;

public sealed partial class SessionsPage : Page
{
    public SessionsPage()
    {
        InitializeComponent();
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.ShowLibrary();
    }
}
