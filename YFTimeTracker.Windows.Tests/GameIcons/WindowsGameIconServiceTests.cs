using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Windows.GameIcons;

namespace YFTimeTracker.Windows.Tests.GameIcons;

[TestClass]
public sealed class WindowsGameIconServiceTests
{
    [TestMethod]
    public async Task Icon_is_cached_and_refreshed_after_executable_changes()
    {
        using var paths = new TempAppPathProvider();
        var executablePath = Path.Combine(paths.DataDirectory, "game.exe");
        await File.WriteAllTextAsync(executablePath, "version-one");
        var extractor = new FakeIconExtractor();
        var service = new WindowsGameIconService(
            paths,
            extractor,
            NullLogger<WindowsGameIconService>.Instance);

        var parallelPaths = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            service.GetIconPathAsync(executablePath, CancellationToken.None)));
        var firstPath = parallelPaths[0];

        Assert.IsNotNull(firstPath);
        Assert.IsTrue(parallelPaths.All(path => path == firstPath));
        Assert.IsTrue(File.Exists(firstPath));
        Assert.AreEqual(1, extractor.CallCount);

        File.SetLastWriteTimeUtc(executablePath, DateTime.UtcNow.AddMinutes(1));
        var refreshedPath = await service.GetIconPathAsync(executablePath, CancellationToken.None);

        Assert.AreEqual(firstPath, refreshedPath);
        Assert.AreEqual(2, extractor.CallCount);
    }

    [TestMethod]
    public async Task Missing_executable_returns_no_icon_without_extracting()
    {
        using var paths = new TempAppPathProvider();
        var extractor = new FakeIconExtractor();
        var service = new WindowsGameIconService(
            paths,
            extractor,
            NullLogger<WindowsGameIconService>.Instance);

        var iconPath = await service.GetIconPathAsync(
            Path.Combine(paths.DataDirectory, "missing.exe"),
            CancellationToken.None);

        Assert.IsNull(iconPath);
        Assert.AreEqual(0, extractor.CallCount);
    }

    private sealed class FakeIconExtractor : IExecutableIconExtractor
    {
        public int CallCount { get; private set; }

        public async Task<bool> ExtractAsync(
            string executablePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await File.WriteAllBytesAsync(destinationPath, [0x89, 0x50, 0x4E, 0x47], cancellationToken);
            return true;
        }
    }

    private sealed class TempAppPathProvider : IAppPathProvider, IDisposable
    {
        public TempAppPathProvider()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-icons-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }

        public string DatabasePath => Path.Combine(DataDirectory, "test.db");

        public string LogDirectory => Path.Combine(DataDirectory, "Logs");

        public string BackupDirectory => Path.Combine(DataDirectory, "Backups");

        public string ExportDirectory => Path.Combine(DataDirectory, "Exports");

        public void Dispose()
        {
            if (Directory.Exists(DataDirectory))
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
        }
    }
}
