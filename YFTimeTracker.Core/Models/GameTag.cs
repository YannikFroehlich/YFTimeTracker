namespace YFTimeTracker.Core.Models;

public sealed class GameTag
{
    public long Id { get; set; }

    public long GameId { get; set; }

    public Game? Game { get; set; }

    public string Tag { get; set; } = string.Empty;
}
