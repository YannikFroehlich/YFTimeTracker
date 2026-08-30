using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace YFTimeTracker.App.Converters;

public sealed class HexColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SolidColorBrush(Color.FromArgb(255, 131, 145, 168));
        }

        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            hex = $"FF{hex}";
        }

        if (hex.Length != 8 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return new SolidColorBrush(Color.FromArgb(255, 131, 145, 168));
        }

        return new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
