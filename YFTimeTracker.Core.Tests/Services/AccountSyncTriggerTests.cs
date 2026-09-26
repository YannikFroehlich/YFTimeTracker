using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class AccountSyncTriggerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Open_sessions_stay_out_of_the_sync_in_both_directions()
    {
        var open = Session("ses:local:open", endedAtUtc: null);
        var closed = Session("ses:local:closed", endedAtUtc: Start.AddHours(1));
        var local = Snapshot(open, closed);
        var remote = AccountSnapshot.Empty with
        {
            Sessions =
            [
                Session("ses:other:open", endedAtUtc: null),
                Session("ses:other:closed", endedAtUtc: Start.AddHours(1)),
                Session("ses:other:deleted-open", endedAtUtc: null) with { DeletedAt = Start }
            ]
        };

        var (filteredLocal, filteredRemote) = AccountSyncService.ExcludeOpenSessions(local, remote);

        CollectionAssert.AreEqual(
            new[] { "ses:local:closed" },
            filteredLocal.Sessions.Entries.Select(entry => entry.Identity).ToArray());
        CollectionAssert.AreEqual(
            new[] { "ses:other:closed", "ses:other:deleted-open" },
            filteredRemote.Sessions.Select(session => session.Identity).ToArray());
    }

    [TestMethod]
    public async Task Sync_runs_when_a_game_starts_or_ends_but_not_on_every_scan()
    {
        var tracking = new FakeTrackingService();
        var accountSync = new CountingAccountSyncService();
        using var trigger = new SessionSyncTrigger(tracking, accountSync);
        trigger.Initialize();

        tracking.Publish();
        tracking.Publish(1);
        await accountSync.WaitForCallsAsync(1);
        tracking.Publish(1);
        tracking.Publish(1);
        tracking.Publish();
        await accountSync.WaitForCallsAsync(2);
        await Task.Delay(100);

        Assert.AreEqual(2, accountSync.Calls);
    }

    private static CloudGameSession Session(string identity, DateTimeOffset? endedAtUtc) =>
        new(identity, null, $"hash-{identity}", "game:steam:440", Start, Start, endedAtUtc, null, "boot");

    private static LocalSyncSnapshot Snapshot(params CloudGameSession[] sessions) =>
        new(
            LocalSyncSet<CloudGame>.Empty,
            LocalSyncSet<CloudExecutable>.Empty,
            LocalSyncSet<CloudTag>.Empty,
            new LocalSyncSet<CloudGameSession>(
                sessions.Select(session => new LocalSyncEntry(session.Identity, 1, null, session.ContentHash, null)).ToList(),
                sessions.ToDictionary(session => session.Identity)),
            LocalSyncSet<CloudExclusion>.Empty,
            LocalSyncSet<CloudSetting>.Empty,
            LocalSyncSet<CloudArtwork>.Empty,
            [],
            new CloudProfile(null, null, Start));

    private sealed class FakeTrackingService : IGameTrackingService
    {
        public TrackingState State { get; private set; } = TrackingState.Stopped;

        public event EventHandler<TrackingState>? StateChanged;

        public void Publish(params long[] runningGameIds)
        {
            State = new TrackingState(true, false, runningGameIds
                .Select(id => new RunningGameInfo(id, $"Spiel {id}", Start, TimeSpan.Zero))
                .ToArray());
            StateChanged?.Invoke(this, State);
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RecoverOpenSessionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ScanOnceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingAccountSyncService : IAccountSyncService
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public event EventHandler<SyncSummary>? SyncCompleted
        {
            add { }
            remove { }
        }

        public Task TrySyncAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }

        public async Task WaitForCallsAsync(int expected)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Calls < expected && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.IsTrue(Calls >= expected, $"Erwartet {expected} Abgleiche, erfolgt {Calls}.");
        }

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<SyncSummary?> SyncNowAsync(IProgress<CloudProgress>? progress, CancellationToken cancellationToken) =>
            Task.FromResult<SyncSummary?>(null);

        public Task<DateTimeOffset?> GetLastSyncAtUtcAsync(CancellationToken cancellationToken) =>
            Task.FromResult<DateTimeOffset?>(null);
    }
}
