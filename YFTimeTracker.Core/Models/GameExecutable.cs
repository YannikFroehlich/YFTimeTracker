namespace YFTimeTracker.Core.Models;

public sealed class GameExecutable
{
    public long Id { get; set; }

    public long GameId { get; set; }

    public Game? Game { get; set; }

    public string ExecutablePath { get; set; } = string.Empty;

    public string ExecutablePathKey { get; set; } = string.Empty;

    public string ExecutableName { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }
}
