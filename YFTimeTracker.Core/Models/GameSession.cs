namespace YFTimeTracker.Core.Models;

public sealed class GameSession
{
    public long Id { get; set; }

    public long GameId { get; set; }

    public Game? Game { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public DateTimeOffset? EndedAtUtc { get; set; }

    public long? DurationSeconds { get; set; }

    public string BootSessionId { get; set; } = string.Empty;

    public bool IsOpen => EndedAtUtc is null;

    public TimeSpan GetEffectiveDuration(DateTimeOffset nowUtc)
    {
        var end = EndedAtUtc ?? nowUtc;
        return end <= StartedAtUtc ? TimeSpan.Zero : end - StartedAtUtc;
    }

    public void Close(DateTimeOffset endedAtUtc)
    {
        if (endedAtUtc < StartedAtUtc)
        {
            endedAtUtc = StartedAtUtc;
        }

        EndedAtUtc = endedAtUtc;
        LastSeenAtUtc = endedAtUtc;
        DurationSeconds = Convert.ToInt64(Math.Floor((endedAtUtc - StartedAtUtc).TotalSeconds));
    }
}
