using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.Tests;

internal sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
