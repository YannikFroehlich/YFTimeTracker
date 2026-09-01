using Microsoft.UI.Xaml;

namespace YFTimeTracker.App.Services;

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public interface IThemeService
{
    AppThemePreference CurrentPreference { get; }

    ElementTheme CurrentTheme { get; }

    event EventHandler<ElementTheme>? ThemeChanged;

    Task InitializeAsync(CancellationToken cancellationToken);

    Task SetThemeAsync(AppThemePreference preference, CancellationToken cancellationToken);
}
