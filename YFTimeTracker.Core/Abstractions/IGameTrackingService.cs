using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

public interface IGameTrackingService : IAsyncDisposable
{
    TrackingState State { get; }

    event EventHandler<TrackingState>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);

    Task RecoverOpenSessionsAsync(CancellationToken cancellationToken);

    Task ScanOnceAsync(CancellationToken cancellationToken);
}
