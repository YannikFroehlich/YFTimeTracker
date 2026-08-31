using System.Collections.ObjectModel;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.ViewModels;

public sealed class GlobalSearchViewModel(
    IGlobalSearchRepository searchRepository,
    IClock clock)
{
    public ObservableCollection<GlobalSearchResultViewModel> Results { get; } = [];

    public async Task SearchAsync(string? searchText, CancellationToken cancellationToken)
    {
        var query = searchText?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            Clear();
            return;
        }

        var searchResults = await searchRepository.SearchAsync(
            query,
            gameCount: 5,
            sessionCount: 5,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<GlobalSearchResultViewModel>();
        items.AddRange(searchResults.Games.Select(game => new GlobalSearchResultViewModel(
            GlobalSearchResultKind.Game,
            game.Name,
            $"{FormatSource(game.Source)} · {game.PrimaryExecutable?.ExecutableName ?? "Keine EXE"}",
            "\uE7FC",
            game.Id,
            null)));
        items.AddRange(searchResults.Sessions.Select(session => new GlobalSearchResultViewModel(
            GlobalSearchResultKind.Session,
            session.Game?.Name ?? "Unbekanntes Spiel",
            $"Session · {TimeZoneInfo.ConvertTime(session.StartedAtUtc, TimeZoneInfo.Local):dd.MM.yyyy, HH:mm} · {TimeFormatter.Format(session.GetEffectiveDuration(clock.UtcNow))}",
            "\uE787",
            session.GameId,
            session.Id)));

        AddNavigationResults(items, query);
        ReplaceResults(items);
    }

    public void Clear()
    {
        Results.Clear();
    }

    private static void AddNavigationResults(ICollection<GlobalSearchResultViewModel> items, string query)
    {
        if (Matches(query, "Bibliothek", "Spiele", "Games"))
        {
            items.Add(new GlobalSearchResultViewModel(
                GlobalSearchResultKind.Library,
                "Bibliothek öffnen",
                "Alle erkannten und manuell angelegten Spiele",
                "\uE8B7",
                null,
                null));
        }

        if (Matches(query, "Sessions", "Sitzungen", "Verlauf"))
        {
            items.Add(new GlobalSearchResultViewModel(
                GlobalSearchResultKind.Sessions,
                "Sessions öffnen",
                "Erfasste Spielzeiten durchsuchen und bearbeiten",
                "\uE787",
                null,
                null));
        }

        if (Matches(query, "Statistiken", "Statistik", "Auswertung", "Spielzeit"))
        {
            items.Add(new GlobalSearchResultViewModel(
                GlobalSearchResultKind.Statistics,
                "Statistiken öffnen",
                "Trends, Verteilung und Aktivität anzeigen",
                "\uE9D2",
                null,
                null));
        }
    }

    private static bool Matches(string query, params string[] candidates)
    {
        return candidates.Any(candidate =>
            candidate.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || query.Contains(candidate, StringComparison.CurrentCultureIgnoreCase));
    }

    private void ReplaceResults(IReadOnlyList<GlobalSearchResultViewModel> items)
    {
        Results.Clear();
        foreach (var item in items)
        {
            Results.Add(item);
        }
    }

    private static string FormatSource(GameSource source)
    {
        return source switch
        {
            GameSource.Steam => "Steam",
            GameSource.Epic => "Epic Games",
            GameSource.Gog => "GOG",
            GameSource.Xbox => "Xbox / Microsoft Store",
            GameSource.BattleNet => "Battle.net",
            GameSource.Ubisoft => "Ubisoft Connect",
            _ => "Manuell"
        };
    }
}

public enum GlobalSearchResultKind
{
    Game,
    Session,
    Library,
    Sessions,
    Statistics
}

public sealed record GlobalSearchResultViewModel(
    GlobalSearchResultKind Kind,
    string Title,
    string Subtitle,
    string Glyph,
    long? GameId,
    long? SessionId)
{
    public override string ToString() => Title;
}
