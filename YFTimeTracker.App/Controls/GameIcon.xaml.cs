using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace YFTimeTracker.App.Controls;

public sealed partial class GameIcon : UserControl
{
    public static readonly DependencyProperty IconPathProperty = DependencyProperty.Register(
        nameof(IconPath),
        typeof(string),
        typeof(GameIcon),
        new PropertyMetadata(null, OnIconPathChanged));

    public static readonly DependencyProperty InitialsProperty = DependencyProperty.Register(
        nameof(Initials),
        typeof(string),
        typeof(GameIcon),
        new PropertyMetadata("?"));

    public static readonly DependencyProperty IconCornerRadiusProperty = DependencyProperty.Register(
        nameof(IconCornerRadius),
        typeof(CornerRadius),
        typeof(GameIcon),
        new PropertyMetadata(new CornerRadius(8)));

    public static readonly DependencyProperty IconPaddingProperty = DependencyProperty.Register(
        nameof(IconPadding),
        typeof(Thickness),
        typeof(GameIcon),
        new PropertyMetadata(new Thickness(4)));

    private int loadVersion;
    private int requestedDecodePixelWidth;
    private XamlRoot? observedXamlRoot;

    public GameIcon()
    {
        InitializeComponent();
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["YFTextBrush"];
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => RefreshIconResolution();
    }

    public string? IconPath
    {
        get => (string?)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public CornerRadius IconCornerRadius
    {
        get => (CornerRadius)GetValue(IconCornerRadiusProperty);
        set => SetValue(IconCornerRadiusProperty, value);
    }

    public Thickness IconPadding
    {
        get => (Thickness)GetValue(IconPaddingProperty);
        set => SetValue(IconPaddingProperty, value);
    }

    private static void OnIconPathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is GameIcon { IsLoaded: true } control)
        {
            _ = control.LoadIconAsync(args.NewValue as string);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        observedXamlRoot = XamlRoot;
        if (observedXamlRoot is not null)
        {
            observedXamlRoot.Changed += OnXamlRootChanged;
        }

        if (IconImage.Source is null)
        {
            _ = LoadIconAsync(IconPath);
        }
        else
        {
            RefreshIconResolution();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (observedXamlRoot is not null)
        {
            observedXamlRoot.Changed -= OnXamlRootChanged;
            observedXamlRoot = null;
        }

        Interlocked.Increment(ref loadVersion);
        requestedDecodePixelWidth = 0;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => RefreshIconResolution();

    private int GetDecodePixelWidth() => IconDecodeSize.ForLogicalSize(
        ActualWidth,
        ActualHeight,
        XamlRoot?.RasterizationScale ?? 1);

    private void RefreshIconResolution()
    {
        if (IsLoaded && IconImage.Source is not null && GetDecodePixelWidth() > requestedDecodePixelWidth)
        {
            _ = LoadIconAsync(IconPath, preserveCurrentImage: true);
        }
    }

    private async Task LoadIconAsync(string? iconPath, bool preserveCurrentImage = false)
    {
        var currentVersion = Interlocked.Increment(ref loadVersion);
        requestedDecodePixelWidth = GetDecodePixelWidth();
        var decodePixelWidth = requestedDecodePixelWidth;
        if (!preserveCurrentImage)
        {
            IconImage.Source = null;
            IconImage.Visibility = Visibility.Collapsed;
            FallbackText.Visibility = Visibility.Visible;
        }

        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(iconPath);
            using var stream = await file.OpenReadAsync();
            var image = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Physical,
                DecodePixelWidth = decodePixelWidth,
            };
            await image.SetSourceAsync(stream);
            if (currentVersion != Volatile.Read(ref loadVersion)
                || !string.Equals(IconPath, iconPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            IconImage.Source = image;
            IconImage.Visibility = Visibility.Visible;
            FallbackText.Visibility = Visibility.Collapsed;
            RefreshIconResolution();
        }
        catch (Exception)
        {
            if (currentVersion == Volatile.Read(ref loadVersion) && !preserveCurrentImage)
            {
                IconImage.Source = null;
                IconImage.Visibility = Visibility.Collapsed;
                FallbackText.Visibility = Visibility.Visible;
            }
        }
    }
}
