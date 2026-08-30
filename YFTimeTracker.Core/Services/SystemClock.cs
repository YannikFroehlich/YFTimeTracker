using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Core.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
