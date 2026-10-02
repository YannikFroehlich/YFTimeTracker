using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YFTimeTracker.App.Views;

/// <summary>
/// Grid rechnet ColumnSpacing und RowSpacing auch für Spalten mit Breite 0 und leere Zeilen
/// ein. Gestapelte Karten würden dadurch schmaler als der Rest der Seite, und unter einem
/// Block entstünden Lücken. Responsive Layouts legen deshalb nur die Spalten und Zeilen an,
/// die sie gerade nutzen.
/// </summary>
internal static class GridDefinitions
{
    public static GridLength Star(double value = 1) => new(value, GridUnitType.Star);

    public static void SetColumns(Grid grid, params GridLength[] widths)
    {
        if (grid.ColumnDefinitions.Select(column => column.Width).SequenceEqual(widths))
        {
            return;
        }

        grid.ColumnDefinitions.Clear();
        foreach (var width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }
    }

    public static void SetRows(Grid grid, int count)
    {
        while (grid.RowDefinitions.Count > count)
        {
            grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
        }

        while (grid.RowDefinitions.Count < count)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
    }
}
