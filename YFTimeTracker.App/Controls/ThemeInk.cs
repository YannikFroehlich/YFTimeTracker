using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YFTimeTracker.App.Converters;

namespace YFTimeTracker.App.Controls;

/// <summary>
/// Setzt die Textfarbe aus einem Hexwert der ViewModels. Im hellen Design sind die Neonfarben
/// auf hellem Grund kaum lesbar (Kontrast teils unter 2:1); dort wird die passende Textfarbe
/// aus den hellen Theme-Ressourcen in App.xaml verwendet. Im dunklen Design bleibt der Hexwert.
/// </summary>
public static class ThemeInk
{
    private static readonly Dictionary<string, string> LightInkKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#29E7A4"] = "YFGreenTextBrush",
        ["#2CE5F3"] = "YFCyanTextBrush",
        ["#3182FF"] = "YFBlueTextBrush",
        ["#387BFF"] = "YFBlueTextBrush",
        ["#8A4DFF"] = "YFPurpleTextBrush",
        ["#FF5368"] = "YFDangerTextBrush",
        ["#FF6B7A"] = "YFDangerTextBrush",
        ["#F5B942"] = "YFWarningTextBrush",
        ["#8391A8"] = "YFMutedBrush",
        ["#9AA8BF"] = "YFMutedBrush",
        ["#A7B2C7"] = "YFMutedBrush"
    };

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground",
        typeof(string),
        typeof(ThemeInk),
        new PropertyMetadata(null, OnForegroundChanged));

    public static string? GetForeground(DependencyObject element) => (string?)element.GetValue(ForegroundProperty);

    public static void SetForeground(DependencyObject element, string? value) => element.SetValue(ForegroundProperty, value);

    private static void OnForegroundChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        // Das Theme steht erst im Baum fest und kann sich zur Laufzeit ändern.
        element.ActualThemeChanged -= Element_ActualThemeChanged;
        element.ActualThemeChanged += Element_ActualThemeChanged;
        element.Loaded -= Element_Loaded;
        element.Loaded += Element_Loaded;
        Apply(element);
    }

    private static void Element_ActualThemeChanged(FrameworkElement sender, object args) => Apply(sender);

    private static void Element_Loaded(object sender, RoutedEventArgs e) => Apply((FrameworkElement)sender);

    private static void Apply(FrameworkElement element)
    {
        var hex = GetForeground(element)?.Trim();
        Brush brush = element.ActualTheme == ElementTheme.Light
            && hex is not null
            && LightInkKeys.TryGetValue(hex, out var key)
            && Application.Current.Resources.ThemeDictionaries["Light"] is ResourceDictionary light
            && light[key] is Brush ink
                ? ink
                : HexColorBrushConverter.ToBrush(hex);

        switch (element)
        {
            case TextBlock text:
                text.Foreground = brush;
                break;
            case IconElement icon:
                icon.Foreground = brush;
                break;
        }
    }
}
