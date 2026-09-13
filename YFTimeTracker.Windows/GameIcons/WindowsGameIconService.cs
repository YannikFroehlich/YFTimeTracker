using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.GameIcons;

public sealed class WindowsGameIconService : IGameIconService
{
    private const long MaximumCoverFileSize = 10 * 1024 * 1024;
    private readonly IAppPathProvider paths;
    private readonly IGameArtworkRepository? artworks;
    private readonly IExecutableIconExtractor extractor;
    private readonly ILogger<WindowsGameIconService> logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> cacheLocks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim extractionSlots = new(4, 4);

    public WindowsGameIconService(
        IAppPathProvider paths,
        IGameArtworkRepository artworks,
        ILogger<WindowsGameIconService> logger)
        : this(paths, new ShellExecutableIconExtractor(), logger, artworks)
    {
    }

    internal WindowsGameIconService(
        IAppPathProvider paths,
        IExecutableIconExtractor extractor,
        ILogger<WindowsGameIconService> logger,
        IGameArtworkRepository? artworks = null)
    {
        this.paths = paths;
        this.extractor = extractor;
        this.logger = logger;
        this.artworks = artworks;
    }

    public async Task<string?> GetGameImagePathAsync(
        long gameId,
        string? executablePath,
        CancellationToken cancellationToken)
    {
        if (artworks is not null
            && await artworks.GetByGameIdAsync(gameId, cancellationToken) is { } artwork)
        {
            var coverPath = await MaterializeCoverAsync(artwork, cancellationToken);
            if (coverPath is not null)
            {
                return coverPath;
            }
        }

        return await GetIconPathAsync(executablePath, cancellationToken);
    }

    public async Task<bool> HasCustomCoverAsync(long gameId, CancellationToken cancellationToken)
    {
        return artworks is not null
            && await artworks.GetByGameIdAsync(gameId, cancellationToken) is not null;
    }

    public async Task<string> SetCustomCoverAsync(
        long gameId,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (artworks is null)
        {
            throw new InvalidOperationException("Der Cover-Speicher ist nicht verfügbar.");
        }

        if (gameId <= 0 || string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Spiel und Cover-Datei müssen angegeben werden.");
        }

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(sourcePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException("Der Pfad zum Cover ist ungültig.", exception);
        }
        var file = new FileInfo(normalizedPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Die ausgewählte Cover-Datei wurde nicht gefunden.", normalizedPath);
        }

        if (file.Length <= 0 || file.Length > MaximumCoverFileSize)
        {
            throw new InvalidDataException("Das Cover muss zwischen 1 Byte und 10 MB groß sein.");
        }

        var imageData = await File.ReadAllBytesAsync(normalizedPath, cancellationToken);
        var imageFormat = DetectImageFormat(imageData)
            ?? throw new InvalidDataException("Bitte wähle ein gültiges PNG- oder JPEG-Bild aus.");
        var sha256 = Convert.ToHexString(SHA256.HashData(imageData));
        var artwork = new GameArtwork
        {
            GameId = gameId,
            ContentType = imageFormat.ContentType,
            FileExtension = imageFormat.FileExtension,
            Sha256 = sha256,
            ImageData = imageData,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await artworks.UpsertAsync(artwork, cancellationToken);
        DeleteCachedCovers(gameId);
        return await MaterializeCoverAsync(artwork, cancellationToken)
            ?? throw new IOException("Das Cover konnte nicht lokal zwischengespeichert werden.");
    }

    public async Task RemoveCustomCoverAsync(long gameId, CancellationToken cancellationToken)
    {
        if (artworks is not null)
        {
            await artworks.DeleteAsync(gameId, cancellationToken);
        }

        DeleteCachedCovers(gameId);
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

    private async Task<string?> MaterializeCoverAsync(GameArtwork artwork, CancellationToken cancellationToken)
    {
        try
        {
            var cacheDirectory = Path.Combine(paths.DataDirectory, "GameCovers");
            var cachePath = Path.Combine(
                cacheDirectory,
                $"{artwork.GameId}-{artwork.Sha256[..Math.Min(16, artwork.Sha256.Length)]}{artwork.FileExtension}");
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length == artwork.ImageData.LongLength)
            {
                return cachePath;
            }

            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllBytesAsync(cachePath, artwork.ImageData, cancellationToken);
            return cachePath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(exception, "Could not materialize the custom cover for game {GameId}.", artwork.GameId);
            return null;
        }
    }

    private void DeleteCachedCovers(long gameId)
    {
        var cacheDirectory = Path.Combine(paths.DataDirectory, "GameCovers");
        if (!Directory.Exists(cacheDirectory))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(cacheDirectory, $"{gameId}-*"))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Could not clear cached custom covers for game {GameId}.", gameId);
        }
    }

    private static (string ContentType, string FileExtension)? DetectImageFormat(byte[] data)
    {
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return ("image/png", ".png");
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ("image/jpeg", ".jpg");
        }

        return null;
    }
}
