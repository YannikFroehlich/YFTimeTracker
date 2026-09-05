using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Services;
using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class WindowsProcessSnapshotProviderTests
{
    [TestMethod]
    public async Task GetRunningProcessesAsync_finds_the_current_process()
    {
        var executablePath = Environment.ProcessPath;
        Assert.IsNotNull(executablePath, "Der Pfad des Testprozesses konnte nicht ermittelt werden.");

        var provider = new WindowsProcessSnapshotProvider(
            NullLogger<WindowsProcessSnapshotProvider>.Instance);

        var processes = await provider.GetRunningProcessesAsync(CancellationToken.None);

        var expectedKey = ExecutablePathNormalizer.CreateKey(executablePath);
        var current = processes.SingleOrDefault(process => process.ExecutablePathKey == expectedKey);
        Assert.IsNotNull(current, "Der eigene Prozess wurde in der Momentaufnahme nicht gefunden.");
        Assert.IsNotNull(current.StartedAtUtc);
        Assert.IsTrue(
            current.StartedAtUtc <= DateTimeOffset.UtcNow,
            "Die Startzeit des eigenen Prozesses darf nicht in der Zukunft liegen.");
    }

    [TestMethod]
    public async Task GetRunningProcessesAsync_returns_distinct_executables_only()
    {
        var provider = new WindowsProcessSnapshotProvider(
            NullLogger<WindowsProcessSnapshotProvider>.Instance);

        var processes = await provider.GetRunningProcessesAsync(CancellationToken.None);

        Assert.IsNotEmpty(processes, "Es wurde kein laufender Prozess erkannt.");
        Assert.AreEqual(
            processes.Count,
            processes.Select(process => process.ExecutablePathKey).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Ein Programmpfad wurde mehrfach zurückgegeben.");
        Assert.IsTrue(
            processes.All(process => process.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)),
            "Die Momentaufnahme enthält Einträge, die keine EXE-Datei sind.");
        Assert.IsTrue(
            processes.All(process => Path.IsPathFullyQualified(process.ExecutablePath)),
            "Die Momentaufnahme enthält einen nicht vollständig qualifizierten Pfad.");
    }

    [TestMethod]
    public async Task GetRunningProcessesAsync_honours_cancellation()
    {
        var provider = new WindowsProcessSnapshotProvider(
            NullLogger<WindowsProcessSnapshotProvider>.Instance);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetRunningProcessesAsync(cancellation.Token));
    }
}
