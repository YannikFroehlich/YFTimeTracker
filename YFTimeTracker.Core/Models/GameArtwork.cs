namespace YFTimeTracker.Core.Models;

public sealed class GameArtwork
{
    public long GameId { get; set; }

    public Game? Game { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public string FileExtension { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public byte[] ImageData { get; set; } = [];

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
