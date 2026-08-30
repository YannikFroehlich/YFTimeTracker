using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.App.Services;

public sealed class AppDiagnosticsService(
    IAppPathProvider paths,
    IAppUpdateService appUpdateService,
    ILogger<AppDiagnosticsService> logger) : IAppDiagnosticsService
{
    private const int MaximumIncludedLogs = 3;

    public AppDiagnosticsSnapshot GetSnapshot()
    {
        var installDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var installRoot = Directory.GetParent(installDirectory)?.FullName;
        var isInstalled = installRoot is not null &&
            (File.Exists(Path.Combine(installRoot, ".msi-installed")) ||
             File.Exists(Path.Combine(installRoot, "Update.exe")));

        return new AppDiagnosticsSnapshot(
            appUpdateService.State.CurrentVersion,
            isInstalled ? "Installiert (Velopack)" : "Portable / Entwicklung",
            installDirectory,
            paths.DataDirectory,
            paths.LogDirectory,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
    }

    public void OpenLogDirectory()
    {
        Directory.CreateDirectory(paths.LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = paths.LogDirectory,
            UseShellExecute = true
        });
    }

    public async Task<AppDiagnosticsExportResult> ExportAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        Directory.CreateDirectory(paths.ExportDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath) ?? paths.ExportDirectory);

        var temporaryPath = Path.Combine(
            paths.ExportDirectory,
            $".YFTimeTracker-Diagnose-{Guid.NewGuid():N}.tmp");
        var logFiles = GetRecentLogFiles();

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteSummaryAsync(archive, logFiles, cancellationToken);
                foreach (var logFile in logFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await AddLogFileAsync(archive, logFile, cancellationToken);
                }
            }

            File.Move(temporaryPath, archivePath, overwrite: true);
            logger.LogInformation(
                "Diagnostic report exported with {LogCount} log files.",
                logFiles.Count);
            return new AppDiagnosticsExportResult(archivePath, logFiles.Count);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Exporting the diagnostic report failed.");
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private List<FileInfo> GetRecentLogFiles()
    {
        if (!Directory.Exists(paths.LogDirectory))
        {
            return [];
        }

        return new DirectoryInfo(paths.LogDirectory)
            .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumIncludedLogs)
            .ToList();
    }

    private async Task WriteSummaryAsync(
        ZipArchive archive,
        IReadOnlyCollection<FileInfo> logFiles,
        CancellationToken cancellationToken)
    {
        var snapshot = GetSnapshot();
        var databaseInfo = File.Exists(paths.DatabasePath)
            ? $"vorhanden ({new FileInfo(paths.DatabasePath).Length / 1024d:0.#} KB)"
            : "nicht vorhanden";
        var lines = new[]
        {
            "YFTimeTracker Diagnosebericht",
            "============================",
            $"Erstellt (lokal): {DateTimeOffset.Now:O}",
            $"Erstellt (UTC):   {DateTimeOffset.UtcNow:O}",
            string.Empty,
            $"App-Version:      {snapshot.Version}",
            $"Verteilung:       {snapshot.Distribution}",
            $".NET-Runtime:     {snapshot.RuntimeDescription}",
            $"Betriebssystem:   {snapshot.OperatingSystemDescription}",
            $"Prozessarchitektur: {snapshot.ProcessArchitecture}",
            string.Empty,
            $"Installationsordner: {snapshot.InstallDirectory}",
            $"Datenordner:        {snapshot.DataDirectory}",
            $"Logordner:          {snapshot.LogDirectory}",
            $"Datenbankstatus:    {databaseInfo}",
            string.Empty,
            $"Enthaltene Logs: {logFiles.Count}",
            "Die Datenbank, Backups, Exporte und Spielsessions sind nicht im Archiv enthalten.",
            "Logdateien können lokale Dateipfade und Namen erkannter Spiele enthalten."
        };

        var entry = archive.CreateEntry("diagnostics.txt", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(string.Join(Environment.NewLine, lines).AsMemory(), cancellationToken);
    }

    private static async Task AddLogFileAsync(
        ZipArchive archive,
        FileInfo logFile,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry($"logs/{logFile.Name}", CompressionLevel.Optimal);
        await using var source = new FileStream(
            logFile.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination, cancellationToken);
    }
}
