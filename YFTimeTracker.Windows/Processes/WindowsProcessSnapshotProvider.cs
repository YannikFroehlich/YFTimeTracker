using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Services;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Processes;

public sealed class WindowsProcessSnapshotProvider(ILogger<WindowsProcessSnapshotProvider> logger) : IProcessSnapshotProvider
{
    public Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(CancellationToken cancellationToken)
    {
        var processes = new Dictionary<string, RunningProcessInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (process)
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) &&
                        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var normalizedPath = ExecutablePathNormalizer.NormalizePath(path);
                        var pathKey = ExecutablePathNormalizer.CreateKey(normalizedPath);
                        DateTimeOffset? startedAtUtc = null;
                        try
                        {
                            startedAtUtc = process.StartTime.ToUniversalTime();
                        }
                        catch (SystemException)
                        {
                        }

                        if (processes.TryGetValue(pathKey, out var existing))
                        {
                            startedAtUtc = existing.StartedAtUtc is null || startedAtUtc is null
                                ? null
                                : DateTimeOffset.Compare(existing.StartedAtUtc.Value, startedAtUtc.Value) <= 0
                                    ? existing.StartedAtUtc
                                    : startedAtUtc;
                        }

                        processes[pathKey] = new RunningProcessInfo(normalizedPath, pathKey, startedAtUtc);
                    }
                }
            }
            catch (Win32Exception ex)
            {
                logger.LogDebug(ex, "Could not inspect process {ProcessId}.", SafeProcessId(process));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogDebug(ex, "Process exited while being inspected.");
            }
            catch (SystemException ex)
            {
                logger.LogDebug(ex, "Could not inspect a running process.");
            }
        }

        return Task.FromResult<IReadOnlyList<RunningProcessInfo>>(processes.Values.ToArray());
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}
