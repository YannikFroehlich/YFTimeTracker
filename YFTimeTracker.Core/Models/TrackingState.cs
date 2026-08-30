namespace YFTimeTracker.Core.Models;

public sealed record TrackingState(
    bool IsRunning,
    bool IsPaused,
    IReadOnlyList<RunningGameInfo> RunningGames)
{
    public static TrackingState Stopped { get; } = new(false, false, Array.Empty<RunningGameInfo>());
}
