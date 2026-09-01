using Microsoft.UI.Xaml;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.Services;

public sealed class ThemeService(ISettingsStore settings) : IThemeService
{
    public AppThemePreference CurrentPreference { get; private set; } = AppThemePreference.Dark;

    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Dark;

    public event EventHandler<ElementTheme>? ThemeChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var raw = await settings.GetAsync(AppSettingKeys.Theme, cancellationToken);
        CurrentPreference = Enum.TryParse<AppThemePreference>(raw, out var preference)
            ? preference
            : AppThemePreference.Dark;
        CurrentTheme = ResolveTheme(CurrentPreference);
    }

    public async Task SetThemeAsync(AppThemePreference preference, CancellationToken cancellationToken)
    {
        await settings.SetAsync(AppSettingKeys.Theme, preference.ToString(), cancellationToken);
        CurrentPreference = preference;
        CurrentTheme = ResolveTheme(preference);
        ThemeChanged?.Invoke(this, CurrentTheme);
    }

    private static ElementTheme ResolveTheme(AppThemePreference preference) => preference switch
    {
        AppThemePreference.Light => ElementTheme.Light,
        AppThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
