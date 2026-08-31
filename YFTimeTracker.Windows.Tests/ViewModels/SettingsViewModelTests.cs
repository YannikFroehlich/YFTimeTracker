using YFTimeTracker.App.ViewModels;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Windows.Tests.ViewModels;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public async Task Failed_import_restores_previously_running_tracking()
    {
        var tracking = new FakeTrackingService(isPaused: false);

        await Assert.ThrowsAsync<YFTimeTrackerException>(() => SettingsViewModel.ImportBackupAsync(
            new FailingBackupService(),
            tracking,
            @"C:\Backups\test.zip",
            CancellationToken.None));

        Assert.AreEqual(1, tracking.PauseCount);
        Assert.AreEqual(1, tracking.ResumeCount);
        Assert.IsFalse(tracking.State.IsPaused);
    }

    [TestMethod]
    public async Task Failed_import_keeps_previously_paused_tracking_paused()
    {
        var tracking = new FakeTrackingService(isPaused: true);

        await Assert.ThrowsAsync<YFTimeTrackerException>(() => SettingsViewModel.ImportBackupAsync(
            new FailingBackupService(),
            tracking,
            @"C:\Backups\test.zip",
            CancellationToken.None));

        Assert.AreEqual(1, tracking.PauseCount);
        Assert.AreEqual(0, tracking.ResumeCount);
        Assert.IsTrue(tracking.State.IsPaused);
    }

    private sealed class FakeTrackingService(bool isPaused) : IGameTrackingService
    {
        public TrackingState State { get; private set; } = new(true, isPaused, []);

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public event EventHandler<TrackingState>? StateChanged;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            PauseCount++;
            State = State with { IsPaused = true };
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            ResumeCount++;
            State = State with { IsPaused = false };
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task RecoverOpenSessionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ScanOnceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingBackupService : IBackupService
    {
        public Task<string?> CreateDailyBackupAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string?> CreatePreMigrationBackupAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task PruneBackupsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ExportResult> ExportAsync(string archivePath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ImportResult> ImportAsync(string archivePath, CancellationToken cancellationToken) =>
            throw new YFTimeTrackerException("Import konnte nicht gelesen werden.");
    }
}
