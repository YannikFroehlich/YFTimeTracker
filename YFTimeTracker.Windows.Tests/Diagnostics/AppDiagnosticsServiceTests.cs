using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Windows.Tests.Diagnostics;

[TestClass]
public sealed class AppDiagnosticsServiceTests
{
    [TestMethod]
    public async Task ExportAsync_IncludesNewestLogsButNoDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-Diagnostics-Test-{Guid.NewGuid():N}");
        var paths = new TestAppPathProvider(root);

        try
        {
            Directory.CreateDirectory(paths.LogDirectory);
            Directory.CreateDirectory(paths.DataDirectory);
            await File.WriteAllTextAsync(paths.DatabasePath, "private database content");

            for (var index = 1; index <= 4; index++)
            {
                var logPath = Path.Combine(paths.LogDirectory, $"log-{index}.log");
                await File.WriteAllTextAsync(logPath, $"log content {index}");
                File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow.AddMinutes(index));
            }

            var trackingDiagnostics = new TrackingDiagnosticLog(new FixedClock(DateTimeOffset.Parse("2026-09-12T10:00:00Z")));
            trackingDiagnostics.Record(
                TrackingDiagnosticEventKind.Session,
                TrackingDiagnosticSeverity.Success,
                "Session gestartet",
                "Neon Game · game.exe");
            var service = new AppDiagnosticsService(
                paths,
                new TestUpdateService("9.8.7"),
                trackingDiagnostics,
                NullLogger<AppDiagnosticsService>.Instance);
            var archivePath = Path.Combine(root, "diagnostics.zip");

            var result = await service.ExportAsync(archivePath, CancellationToken.None);

            Assert.AreEqual(3, result.IncludedLogCount);
            using var archive = ZipFile.OpenRead(archivePath);
            var entryNames = archive.Entries.Select(entry => entry.FullName).ToArray();
            CollectionAssert.Contains(entryNames, "diagnostics.txt");
            CollectionAssert.Contains(entryNames, "tracking-events.txt");
            CollectionAssert.Contains(entryNames, "logs/log-4.log");
            CollectionAssert.Contains(entryNames, "logs/log-3.log");
            CollectionAssert.Contains(entryNames, "logs/log-2.log");
            CollectionAssert.DoesNotContain(entryNames, "logs/log-1.log");
            Assert.IsFalse(entryNames.Any(name => name.Contains(".db", StringComparison.OrdinalIgnoreCase)));

            var summaryEntry = archive.GetEntry("diagnostics.txt");
            Assert.IsNotNull(summaryEntry);
            using var reader = new StreamReader(summaryEntry.Open());
            var summary = await reader.ReadToEndAsync();
            StringAssert.Contains(summary, "App-Version:      9.8.7");
            StringAssert.Contains(summary, "Tracking-Ereignisse: 1");
            StringAssert.Contains(summary, "Die Datenbank, Backups, Exporte und Spielsessions sind nicht im Archiv enthalten.");

            var trackingEntry = archive.GetEntry("tracking-events.txt");
            Assert.IsNotNull(trackingEntry);
            using var trackingReader = new StreamReader(trackingEntry.Open());
            var trackingText = await trackingReader.ReadToEndAsync();
            StringAssert.Contains(trackingText, "Session gestartet");
            StringAssert.Contains(trackingText, "Neon Game · game.exe");
            Assert.IsFalse(trackingText.Contains(@"C:\", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class TestAppPathProvider(string root) : IAppPathProvider
    {
        public string DataDirectory { get; } = Path.Combine(root, "Data");

        public string DatabasePath => Path.Combine(DataDirectory, "yftimetracker.db");

        public string LogDirectory => Path.Combine(DataDirectory, "Logs");

        public string BackupDirectory => Path.Combine(DataDirectory, "Backups");

        public string ExportDirectory => Path.Combine(DataDirectory, "Exports");
    }

    private sealed class TestUpdateService(string version) : IAppUpdateService
    {
        public event EventHandler<AppUpdateState>? StateChanged
        {
            add { }
            remove { }
        }

        public AppUpdateState State { get; } = new(AppUpdateStage.Idle, version, "Bereit");

        public Task<AppUpdateState> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<AppUpdateState> DownloadUpdateAsync(
            IProgress<int>? progress,
            CancellationToken cancellationToken) => Task.FromResult(State);

        public void ScheduleInstallAndRestart()
        {
        }
    }
}
