namespace YFTimeTracker.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
