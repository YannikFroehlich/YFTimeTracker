namespace YFTimeTracker.Core.Models;

public sealed record GlobalSearchResults(
    IReadOnlyList<Game> Games,
    IReadOnlyList<GameSession> Sessions)
{
    public static GlobalSearchResults Empty { get; } = new([], []);
}
