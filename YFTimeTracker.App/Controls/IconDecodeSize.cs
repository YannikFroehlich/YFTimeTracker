namespace YFTimeTracker.App.Controls;

internal static class IconDecodeSize
{
    private const int MaximumPixelWidth = 1024;
    private const int PixelStep = 16;

    public static int ForLogicalSize(double width, double height, double rasterizationScale)
    {
        var logicalSize = Math.Max(
            double.IsFinite(width) && width > 0 ? width : 0,
            double.IsFinite(height) && height > 0 ? height : 0);
        if (logicalSize == 0)
        {
            logicalSize = 96;
        }

        var scale = double.IsFinite(rasterizationScale) && rasterizationScale > 0
            ? rasterizationScale
            : 1;
        var physicalSize = Math.Clamp(logicalSize * scale, 32, MaximumPixelWidth);

        // Round up so fractional DPI never undersamples; small resize steps reuse the bitmap.
        return (int)(Math.Ceiling(physicalSize / PixelStep) * PixelStep);
    }
}
