namespace YFTimeTracker.Core.Abstractions;

using YFTimeTracker.Core.Models;

public interface IProcessSnapshotProvider
{
    Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(CancellationToken cancellationToken);
}
