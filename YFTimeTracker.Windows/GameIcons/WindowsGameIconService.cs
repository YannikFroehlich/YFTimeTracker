using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.GameIcons;

public sealed class WindowsGameIconService : IGameIconService
{
    private readonly IAppPathProvider paths;
    private readonly IExecutableIconExtractor extractor;
    private readonly ILogger<WindowsGameIconService> logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> cacheLocks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim extractionSlots = new(4, 4);

    public WindowsGameIconService(
        IAppPathProvider paths,
        ILogger<WindowsGameIconService> logger)
        : this(paths, new ShellExecutableIconExtractor(), logger)
    {
    }

    internal WindowsGameIconService(
        IAppPathProvider paths,
        IExecutableIconExtractor extractor,
        ILogger<WindowsGameIconService> logger)
    {
        this.paths = paths;
        this.extractor = extractor;
        this.logger = logger;
    }

    public async Task<string?> GetIconPathAsync(string? executablePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(executablePath);
            if (!File.Exists(normalizedPath))
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogDebug(exception, "Game icon path is invalid.");
            return null;
        }

        var cacheDirectory = Path.Combine(paths.DataDirectory, "GameIcons");
        var cacheKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(normalizedPath.ToUpperInvariant())));
        var cachePath = Path.Combine(cacheDirectory, $"{cacheKey}.png");
        if (IsCurrent(cachePath, normalizedPath))
        {
            return cachePath;
        }

        var cacheLock = cacheLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (IsCurrent(cachePath, normalizedPath))
            {
                return cachePath;
            }

            Directory.CreateDirectory(cacheDirectory);
            var temporaryPath = Path.Combine(cacheDirectory, $"{cacheKey}.{Guid.NewGuid():N}.tmp");
            try
            {
                await extractionSlots.WaitAsync(cancellationToken);
                bool extracted;
                try
                {
                    extracted = await extractor.ExtractAsync(normalizedPath, temporaryPath, cancellationToken);
                }
                finally
                {
                    extractionSlots.Release();
                }

                if (!extracted || !File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
                {
                    return File.Exists(cachePath) ? cachePath : null;
                }

                File.Move(temporaryPath, cachePath, overwrite: true);
                File.SetLastWriteTimeUtc(cachePath, File.GetLastWriteTimeUtc(normalizedPath));
                return cachePath;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not extract a local game icon.");
            return File.Exists(cachePath) ? cachePath : null;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static bool IsCurrent(string cachePath, string executablePath)
    {
        try
        {
            return File.Exists(cachePath)
                && new FileInfo(cachePath).Length > 0
                && File.GetLastWriteTimeUtc(cachePath) >= File.GetLastWriteTimeUtc(executablePath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
