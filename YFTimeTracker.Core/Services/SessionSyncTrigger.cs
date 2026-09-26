using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

/// <summary>
/// Gleicht mit dem Konto ab, sobald ein Spiel startet oder endet, statt erst
/// beim naechsten App-Start.
///
/// Laeuft im Hintergrund und nie im Scan-Takt: der Abgleich darf das Tracking
/// weder aufhalten noch an einem nicht erreichbaren Supabase scheitern lassen.
/// Aendert sich der Stand, waehrend ein Abgleich laeuft, folgt genau ein
/// weiterer Lauf statt einer Warteschlange.
/// </summary>
public sealed class SessionSyncTrigger(
    IGameTrackingService trackingService,
    IAccountSyncService accountSync,
    ILogger<SessionSyncTrigger>? logger = null) : IDisposable
{
    private readonly ILogger<SessionSyncTrigger> log = logger ?? NullLogger<SessionSyncTrigger>.Instance;
    private readonly object sync = new();
    private HashSet<long> runningGameIds = [];
    private bool initialized;
    private bool syncRunning;
    private bool syncPending;

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        trackingService.StateChanged += TrackingService_StateChanged;
    }

    public void Dispose()
    {
        trackingService.StateChanged -= TrackingService_StateChanged;
    }

    private void TrackingService_StateChanged(object? sender, TrackingState state)
    {
        var current = state.RunningGames.Select(game => game.GameId).ToHashSet();
        lock (sync)
        {
            // Jeder Scan meldet den Stand; abgeglichen wird nur, wenn ein Spiel
            // dazukam oder wegfiel.
            if (current.SetEquals(runningGameIds))
            {
                return;
            }

            runningGameIds = current;
            if (syncRunning)
            {
                syncPending = true;
                return;
            }

            syncRunning = true;
        }

        _ = Task.Run(SyncUntilIdleAsync);
    }

    private async Task SyncUntilIdleAsync()
    {
        while (true)
        {
            try
            {
                await accountSync.TrySyncAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                log.LogWarning(exception, "Abgleich nach Spielstart oder -ende fehlgeschlagen");
            }

            lock (sync)
            {
                if (!syncPending)
                {
                    syncRunning = false;
                    return;
                }

                syncPending = false;
            }
        }
    }
}
