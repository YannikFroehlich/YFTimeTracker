using System.Buffers;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Windows.Processes;

public sealed class WindowsProcessSnapshotProvider(ILogger<WindowsProcessSnapshotProvider> logger) : IProcessSnapshotProvider
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;
    private const int InitialPathBufferLength = 1024;
    private const int MaximumPathBufferLength = 32768;
    private const int InitialProcessIdCapacity = 1024;
    private const int MaximumProcessIdCapacity = 32768;

    public Task<IReadOnlyList<RunningProcessInfo>> GetRunningProcessesAsync(CancellationToken cancellationToken)
    {
        var processes = new Dictionary<string, RunningProcessInfo>(StringComparer.OrdinalIgnoreCase);
        var pathBuffer = ArrayPool<char>.Shared.Rent(InitialPathBufferLength);

        try
        {
            foreach (var processId in EnumerateProcessIds())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (processId == 0)
                {
                    continue;
                }

                try
                {
                    Add(processes, processId, ref pathBuffer);
                }
                catch (Exception exception) when (exception
                    is ArgumentException or IOException or NotSupportedException or SecurityException)
                {
                    logger.LogDebug(exception, "Could not inspect process {ProcessId}.", processId);
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(pathBuffer);
        }

        return Task.FromResult<IReadOnlyList<RunningProcessInfo>>(processes.Values.ToArray());
    }

    private void Add(
        Dictionary<string, RunningProcessInfo> processes,
        uint processId,
        ref char[] pathBuffer)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION genügt auch für Prozesse mit höheren Rechten und für
        // Spiele mit Anti-Cheat-Schutz, bei denen sich die Modulliste nicht auslesen lässt.
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Beendete Prozesse bleiben sichtbar, solange ein anderes Programm noch ein Handle
            // darauf hält (etwa der Launcher eines gerade beendeten Spiels).
            ReadProcessTimes(handle, out var startedAtUtc, out var hasExited);
            if (hasExited)
            {
                return;
            }

            var path = QueryImagePath(handle, ref pathBuffer);
            if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var normalizedPath = ExecutablePathNormalizer.NormalizePath(path);
            var pathKey = ExecutablePathNormalizer.CreateKey(normalizedPath);

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
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? QueryImagePath(IntPtr processHandle, ref char[] pathBuffer)
    {
        while (true)
        {
            var size = (uint)pathBuffer.Length;
            if (QueryFullProcessImageName(processHandle, 0, pathBuffer, ref size))
            {
                return size == 0 ? null : new string(pathBuffer, 0, (int)size);
            }

            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer
                || pathBuffer.Length >= MaximumPathBufferLength)
            {
                return null;
            }

            var grown = ArrayPool<char>.Shared.Rent(pathBuffer.Length * 2);
            ArrayPool<char>.Shared.Return(pathBuffer);
            pathBuffer = grown;
        }
    }

    private static void ReadProcessTimes(
        IntPtr processHandle,
        out DateTimeOffset? startedAtUtc,
        out bool hasExited)
    {
        startedAtUtc = null;
        hasExited = false;

        if (!GetProcessTimes(processHandle, out var creationTime, out var exitTime, out _, out _))
        {
            return;
        }

        hasExited = exitTime != 0;
        if (creationTime <= 0)
        {
            return;
        }

        try
        {
            startedAtUtc = new DateTimeOffset(DateTime.FromFileTimeUtc(creationTime));
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private uint[] EnumerateProcessIds()
    {
        var capacity = InitialProcessIdCapacity;

        while (true)
        {
            var buffer = new uint[capacity];
            if (!EnumProcesses(buffer, (uint)(buffer.Length * sizeof(uint)), out var bytesReturned))
            {
                logger.LogWarning(
                    "Could not enumerate running processes (Win32 error {ErrorCode}).",
                    Marshal.GetLastWin32Error());
                return [];
            }

            var count = (int)(bytesReturned / sizeof(uint));
            if (count < buffer.Length || capacity >= MaximumProcessIdCapacity)
            {
                return buffer[..count];
            }

            capacity *= 2;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "K32EnumProcesses", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumProcesses(
        [Out] uint[] processIds,
        uint arraySizeInBytes,
        out uint bytesReturned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        uint flags,
        [Out] char[] exeName,
        ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr process,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
