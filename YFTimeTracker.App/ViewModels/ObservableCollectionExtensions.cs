using System.Collections.ObjectModel;

namespace YFTimeTracker.App.ViewModels;

internal static class ObservableCollectionExtensions
{
    /// <summary>
    /// Bringt die Liste in place auf den Stand von <paramref name="items"/>, statt sie zu leeren
    /// und neu zu füllen. Unveränderte Einträge behalten so ihren Container: Listen flackern bei
    /// Hintergrund-Refreshes nicht, und Auswahl sowie Scrollposition bleiben erhalten.
    /// </summary>
    public static void SyncWith<T>(this ObservableCollection<T> target, IReadOnlyList<T> items)
        where T : class
    {
        var wanted = new HashSet<T>(items, ReferenceEqualityComparer.Instance);
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(target[index]))
            {
                target.RemoveAt(index);
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (index < target.Count && ReferenceEquals(target[index], items[index]))
            {
                continue;
            }

            var existingIndex = target.IndexOf(items[index]);
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
            }
            else
            {
                target.Insert(index, items[index]);
            }
        }
    }
}
