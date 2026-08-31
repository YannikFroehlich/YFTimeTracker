using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using YFTimeTracker.App.ViewModels;

namespace YFTimeTracker.App.Converters;

public sealed class ArcGeometryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var geometry = new PathGeometry();
        if (value is not GameShareSliceViewModel slice)
        {
            return geometry;
        }

        var figure = new PathFigure { StartPoint = slice.ArcStart, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = slice.ArcEnd,
            Size = new Size(slice.Radius, slice.Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = slice.IsLargeArc
        });
        geometry.Figures.Add(figure);
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
