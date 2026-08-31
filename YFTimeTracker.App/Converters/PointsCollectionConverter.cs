using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace YFTimeTracker.App.Converters;

public sealed class PointsCollectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var collection = new PointCollection();
        if (value is IEnumerable<Point> points)
        {
            foreach (var point in points)
            {
                collection.Add(point);
            }
        }

        return collection;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
