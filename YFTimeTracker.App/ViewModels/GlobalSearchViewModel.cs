using System.Collections.ObjectModel;
using System.Text.Json;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.App.ViewModels;

public sealed class GlobalSearchViewModel(
    IGlobalSearchRepository searchRepository,
    IClock clock,
    IGameIconService? gameIcons = null,
    ISettingsStore? settings = null)
{
    private const int MaximumRecentQueries = 6;
    private List<string>? recentQueries;

    public ObservableCollection<GlobalSearchResultViewModel> Results { get; } = [];

    public Task SearchAsync(string? searchText, CancellationToken cancellationToken)
    {
        return SearchAsync(searchText, null, null, cancellationToken);
    }

    public async Task SearchAsync(
        string? searchText,
        GameSource? source,
        TimeSpan? sessionAge,
        CancellationToken cancellationToken)
    {
        var query = searchText?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            Clear();
            return;
        }

        var searchResults = await searchRepository.SearchAsync(
            new GlobalSearchQuery(
                query,
                source,
                sessionAge is { } age ? clock.UtcNow - age : null),
            gameCount: 5,
            sessionCount: 5,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var gameIconPaths = gameIcons is null
            ? new string?[searchResults.Games.Count]
            : await Task.WhenAll(searchResults.Games.Select(game => gameIcons.GetIconPathAsync(
                game.PrimaryExecutable?.ExecutablePath,
                cancellationToken)));
        var sessionIconPaths = gameIcons is null
            ? new string?[searchResults.Sessions.Count]
            : await Task.WhenAll(searchResults.Sessions.Select(session => gameIcons.GetIconPathAsync(
                session.Game?.PrimaryExecutable?.ExecutablePath,
                cancellationToken)));
        cancellationToken.ThrowIfCancellationRequested();

        var items = new List<GlobalSearchResultViewModel>();
        items.AddRange(searchResults.Games.Select((game, index) => new GlobalSearchResultViewModel(
            GlobalSearchResultKind.Game,
            game.Name,
            $"{FormatSource(game.Source)} · {game.PrimaryExecutable?.ExecutableName ?? "Keine EXE"}",
            "\uE7FC",
            game.Id,
            null,
            gameIconPaths[index])));
        items.AddRange(searchResults.Sessions.Select((session, index) => new GlobalSearchResultViewModel(
            GlobalSearchResultKind.Session,
            session.Game?.Name ?? "Unbekanntes Spiel",
            $"Session · {TimeZoneInfo.ConvertTime(session.StartedAtUtc, TimeZoneInfo.Local):dd.MM.yyyy, HH:mm} · {TimeFormatter.Format(session.GetEffectiveDuration(clock.UtcNow))}",
            "\uE787",
            session.GameId,
            session.Id,
            sessionIconPaths[index])));

        AddNavigationResults(items, query);
        ReplaceResults(items);
    }

    public async Task ShowRecentSearchesAsync(CancellationToken cancellationToken)
    {
        var queries = await GetRecentQueriesAsync(cancellationToken);
        ReplaceResults(queries
            .Select(query => new GlobalSearchResultViewModel(
                GlobalSearchResultKind.RecentSearch,
                query,
                "Zuletzt gesucht",
                "\uE823",
                null,
                null,
                SearchText: query))
            .ToList());
    }

    public async Task RememberSearchAsync(string? searchText, CancellationToken cancellationToken)
    {
        var query = searchText?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            return;
        }

        var queries = await GetRecentQueriesAsync(cancellationToken);
        queries.RemoveAll(item => string.Equals(item, query, StringComparison.CurrentCultureIgnoreCase));
        queries.Insert(0, query);
        if (queries.Count > MaximumRecentQueries)
        {
            queries.RemoveRange(MaximumRecentQueries, queries.Count - MaximumRecentQueries);
        }

        if (settings is not null)
        {
            await settings.SetAsync(
                AppSettingKeys.GlobalSearchRecentQueries,
                JsonSerializer.Serialize(queries),
                cancellationToken);
        }
    }

    public async Task ClearRecentSearchesAsync(CancellationToken cancellationToken)
    {
        var queries = await GetRecentQueriesAsync(cancellationToken);
        queries.Clear();
        if (settings is not null)
        {
            await settings.SetAsync(
                AppSettingKeys.GlobalSearchRecentQueries,
                "[]",
                cancellationToken);
        }

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

        if (Matches(query, "Jahresrückblick", "Rückblick", "Jahresreview", "Jahr"))
        {
            items.Add(new GlobalSearchResultViewModel(
                GlobalSearchResultKind.YearReview,
                "Jahresrückblick öffnen",
                "Monate, Rekorde und meistgespielte Spiele eines Jahres",
                "\uE787",
                null,
                null));
        }
    }

    private static bool Matches(string query, params string[] candidates)
    {
        return candidates.Any(candidate =>
            query.Contains(candidate, StringComparison.CurrentCultureIgnoreCase)
            || FuzzySearchMatcher.GetScore(query, candidate) is not null);
    }

    private async Task<List<string>> GetRecentQueriesAsync(CancellationToken cancellationToken)
    {
        if (recentQueries is not null)
        {
            return recentQueries;
        }

        if (settings is null)
        {
            recentQueries = [];
            return recentQueries;
        }

        var serialized = await settings.GetAsync(AppSettingKeys.GlobalSearchRecentQueries, cancellationToken);
        try
        {
            recentQueries = string.IsNullOrWhiteSpace(serialized)
                ? []
                : JsonSerializer.Deserialize<List<string>>(serialized) ?? [];
        }
        catch (JsonException)
        {
            recentQueries = [];
        }

        recentQueries = recentQueries
            .Select(query => query.Trim())
            .Where(query => query.Length >= 2)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(MaximumRecentQueries)
            .ToList();
        return recentQueries;
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
    RecentSearch,
    Game,
    Session,
    Library,
    Sessions,
    Statistics,
    YearReview
}

public sealed record GlobalSearchResultViewModel(
    GlobalSearchResultKind Kind,
    string Title,
    string Subtitle,
    string Glyph,
    long? GameId,
    long? SessionId,
    string? IconPath = null,
    string? SearchText = null)
{
    public string IconText => GameId is null ? Glyph : GetInitials(Title);

    public string IconFontFamily => GameId is null ? "Segoe Fluent Icons" : "Segoe UI Variable Display";

    public override string ToString() => Title;

    private static string GetInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0
            ? "?"
            : string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}
