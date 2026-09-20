using System.Globalization;

namespace YFTimeTracker.App.ViewModels;

public static class TimeFormatter
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    public static string Format(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours:0} h {duration.Minutes:00} min";
        }

        return $"{duration.Minutes:0} min";
    }

    public static string FormatCompact(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (duration.TotalHours >= 1)
        {
            return $"{duration.TotalHours.ToString("0.#", GermanCulture)} h";
        }

        return $"{duration.Minutes:0} min";
    }

    public static string FormatClock(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
    }
}
