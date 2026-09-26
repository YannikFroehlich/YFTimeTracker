using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

/// <summary>
/// Gleicht den lokalen Datenbestand mit dem Konto ab.
///
/// Ablauf je Lauf: lokalen Stand lesen, Kontostand holen, je Datensatzart einen
/// Plan rechnen (<see cref="SyncPlanner"/>), das Ergebnis hochladen und die
/// Gegenrichtung lokal uebernehmen.
///
/// Zwei Festlegungen praegen das Verhalten:
/// - Erst hochladen, dann uebernehmen. Bricht der Lauf dazwischen ab, sind die
///   eigenen Daten bereits gesichert; der lokale Bestand bleibt unveraendert und
///   der naechste Lauf holt die Gegenrichtung nach.
/// - Der Abgleich ist nie Voraussetzung fuer das Tracking. Faellt Supabase aus,
///   laeuft die App unveraendert lokal weiter.
/// </summary>
public sealed class AccountSyncService(
    ISettingsStore settingsStore,
    ICloudAuthService authService,
    IAccountSyncStore syncStore,
    IAccountSyncClient syncClient,
    IDeviceIdentityProvider deviceIdentity,
    IClock clock,
    ILogger<AccountSyncService>? logger = null) : IAccountSyncService
{
    private readonly ILogger<AccountSyncService> log = logger ?? NullLogger<AccountSyncService>.Instance;
    private readonly SemaphoreSlim gate = new(1, 1);

    public event EventHandler<SyncSummary>? SyncCompleted;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        if (!authService.IsSignedIn)
        {
            return false;
        }

        var token = await authService.GetAccessTokenAsync(cancellationToken);
        return !string.IsNullOrEmpty(token);
    }

    public async Task<DateTimeOffset?> GetLastSyncAtUtcAsync(CancellationToken cancellationToken)
    {
        var raw = await settingsStore.GetAsync(AppSettingKeys.CloudLastSyncUtc, cancellationToken);
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    public async Task<SyncSummary?> SyncNowAsync(
        IProgress<CloudProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!await IsReadyAsync(cancellationToken))
        {
            return null;
        }

        // Zwei gleichzeitige Laeufe wuerden denselben Datensatz in beide
        // Richtungen schieben und sich gegenseitig ueberschreiben.
        await gate.WaitAsync(cancellationToken);
        try
        {
            progress?.Report(new CloudProgress("Lokale Daten werden gelesen", 0, 4));
            var local = await syncStore.ReadAsync(deviceIdentity.MachineKey, cancellationToken);

            progress?.Report(new CloudProgress("Konto wird abgefragt", 1, 4));
            var remote = await syncClient.FetchAsync(progress, cancellationToken);
            (local, remote) = ExcludeOpenSessions(local, remote);

            var plans = BuildPlans(local, remote);

            progress?.Report(new CloudProgress("Änderungen werden hochgeladen", 2, 4));
            var push = BuildPush(local, plans);
            if (push.Upserts.TotalCount > 0 || push.DeletedIdentities.Any(entry => entry.Value.Count > 0))
            {
                await syncClient.PushAsync(push, progress, cancellationToken);
            }

            progress?.Report(new CloudProgress("Änderungen werden übernommen", 3, 4));
            var apply = await BuildApplyAsync(local, remote, plans, push, cancellationToken);
            await syncStore.ApplyAsync(apply, cancellationToken);

            var summary = new SyncSummary(
                push.Upserts.TotalCount,
                apply.Upserts.TotalCount,
                apply.RemoveIdentities.Sum(entry => entry.Value.Count),
                push.DeletedIdentities.Sum(entry => entry.Value.Count),
                plans.SelectMany(plan => plan.Value.Conflicts).ToList(),
                clock.UtcNow);

            await settingsStore.SetAsync(
                AppSettingKeys.CloudLastSyncUtc, summary.CompletedAtUtc.ToString("O"), cancellationToken);

            await UpdateKnownDevicesAsync(cancellationToken);

            if (summary.Conflicts.Count > 0)
            {
                // Nicht still ueberschreiben: der Benutzer soll erfahren, dass ein
                // hier gemachter Stand zugunsten des Kontos verworfen wurde.
                log.LogWarning(
                    "Abgleich mit {Count} Konflikt(en) abgeschlossen; der Stand aus dem Konto hat gewonnen: {Identities}",
                    summary.Conflicts.Count,
                    string.Join(", ", summary.Conflicts.Take(10).Select(conflict => conflict.Identity)));
            }

            log.LogInformation(
                "Abgleich abgeschlossen: {Up} hoch, {Down} runter, {DelLocal} lokal entfernt, {DelRemote} im Konto entfernt",
                summary.Uploaded, summary.Downloaded, summary.DeletedLocally, summary.DeletedRemotely);

            SyncCompleted?.Invoke(this, summary);
            return summary;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Haelt die Geraetenamen aus dem Konto lokal vor, damit eine Session eines
    /// zweiten PCs auch offline benannt werden kann. Schlaegt das fehl, bleibt
    /// der bisherige Stand stehen - ein fehlender Name darf keinen Abgleich
    /// scheitern lassen, der sonst durchgelaufen ist.
    /// </summary>
    private async Task UpdateKnownDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var devices = await syncClient.FetchDevicesAsync(cancellationToken);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var device in devices)
            {
                map[device.MachineKey] = device.DeviceName;
            }

            map[deviceIdentity.MachineKey] = deviceIdentity.DeviceName;
            await settingsStore.SetAsync(
                AppSettingKeys.CloudKnownDevices, DeviceDirectory.Serialize(map), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            log.LogWarning(exception, "Geräteliste konnte nicht gelesen werden; bisherige Namen bleiben erhalten");
        }
    }

    public async Task TrySyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncNowAsync(progress: null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            log.LogWarning(exception, "Abgleich mit dem Konto fehlgeschlagen; die App läuft lokal weiter");
        }
    }

    // -------------------------------------------------------------------------
    // Planung
    // -------------------------------------------------------------------------

    /// <summary>
    /// Nimmt laufende Sessions in beide Richtungen aus dem Abgleich.
    ///
    /// Eine offene Session gehoert dem Geraet, auf dem das Spiel laeuft. Laege
    /// sie im Konto, uebernaehme ein zweiter PC sie als offen, faende dort
    /// keinen passenden Prozess und schloesse sie; der naechste Abgleich
    /// schriebe dieses Ende auf das spielende Geraet zurueck und teilte dort die
    /// laufende Session. Deshalb wandert eine Session erst, wenn sie beendet ist.
    /// Geloeschte offene Sessions aus dem Konto bleiben drin, damit die
    /// Loeschung ankommt.
    /// </summary>
    public static (LocalSyncSnapshot Local, AccountSnapshot Remote) ExcludeOpenSessions(
        LocalSyncSnapshot local,
        AccountSnapshot remote)
    {
        var closedLocal = local.Sessions.Entries
            .Where(entry => local.Sessions.ByIdentity.TryGetValue(entry.Identity, out var session)
                && session.EndedAtUtc is not null)
            .ToList();
        var closedRemote = remote.Sessions
            .Where(session => session.EndedAtUtc is not null || session.DeletedAt is not null)
            .ToList();

        return (
            local with { Sessions = local.Sessions with { Entries = closedLocal } },
            remote with { Sessions = closedRemote });
    }

    private static Dictionary<SyncEntityKind, SyncPlan> BuildPlans(LocalSyncSnapshot local, AccountSnapshot remote)
    {
        var tombstonesByKind = local.Tombstones
            .GroupBy(tombstone => KindOf(tombstone.Identity))
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<LocalTombstone>)group.ToList());

        IReadOnlyCollection<LocalTombstone> Tombstones(SyncEntityKind kind) =>
            tombstonesByKind.GetValueOrDefault(kind, []);

        return new Dictionary<SyncEntityKind, SyncPlan>
        {
            [SyncEntityKind.Game] = SyncPlanner.Plan(
                SyncEntityKind.Game, local.Games.Entries, Remote(remote.Games, item => item.Identity, item => item.ContentHash, item => item.CloudId, item => item.DeletedAt, item => item.UpdatedAt), Tombstones(SyncEntityKind.Game)),
            [SyncEntityKind.Executable] = SyncPlanner.Plan(
                SyncEntityKind.Executable, local.Executables.Entries, Remote(remote.Executables, item => item.Identity, item => item.ContentHash, item => item.CloudId, item => item.DeletedAt, item => item.UpdatedAt), Tombstones(SyncEntityKind.Executable)),
            [SyncEntityKind.Tag] = SyncPlanner.Plan(
                SyncEntityKind.Tag, local.Tags.Entries, Remote(remote.Tags, item => item.Identity, item => item.ContentHash, item => item.CloudId, item => item.DeletedAt, item => item.UpdatedAt), Tombstones(SyncEntityKind.Tag)),
            [SyncEntityKind.Session] = SyncPlanner.Plan(
                SyncEntityKind.Session, local.Sessions.Entries, Remote(remote.Sessions, item => item.Identity, item => item.ContentHash, item => item.CloudId, item => item.DeletedAt, item => item.UpdatedAt), Tombstones(SyncEntityKind.Session)),
            [SyncEntityKind.Exclusion] = SyncPlanner.Plan(
                SyncEntityKind.Exclusion, local.Exclusions.Entries, Remote(remote.Exclusions, item => item.Identity, item => item.ContentHash, item => item.CloudId, item => item.DeletedAt, item => item.UpdatedAt), Tombstones(SyncEntityKind.Exclusion)),
            [SyncEntityKind.Setting] = SyncPlanner.Plan(
                SyncEntityKind.Setting, local.Settings.Entries, Remote(remote.Settings, item => item.Identity, item => item.ContentHash, item => item.CloudId, item => item.DeletedAt, item => item.UpdatedAt), Tombstones(SyncEntityKind.Setting)),
            [SyncEntityKind.Artwork] = SyncPlanner.Plan(
                SyncEntityKind.Artwork, local.Artworks.Entries, Remote(remote.Artworks, item => item.Identity, item => item.ContentHash, item => item.CloudId, item => item.DeletedAt, item => item.UpdatedAt), Tombstones(SyncEntityKind.Artwork))
        };
    }

    private static IReadOnlyCollection<RemoteSyncEntry> Remote<T>(
        IReadOnlyList<T> items,
        Func<T, string> identity,
        Func<T, string> contentHash,
        Func<T, string?> cloudId,
        Func<T, DateTimeOffset?> deletedAt,
        Func<T, DateTimeOffset> updatedAt) =>
        items.Select(item => new RemoteSyncEntry(
            identity(item),
            cloudId(item) ?? string.Empty,
            contentHash(item),
            updatedAt(item),
            deletedAt(item) is not null)).ToList();

    /// <summary>
    /// Die Datensatzart steckt im Praefix der Identitaet. Damit kommt der
    /// Grabstein ohne eigene Spalte fuer die Art aus - die Identitaet allein
    /// genuegt, um ihn der richtigen Tabelle zuzuordnen.
    /// </summary>
    private static SyncEntityKind KindOf(string identity) => identity.Split(':', 2)[0] switch
    {
        "game" => SyncEntityKind.Game,
        "exe" => SyncEntityKind.Executable,
        "tag" => SyncEntityKind.Tag,
        "ses" => SyncEntityKind.Session,
        "art" => SyncEntityKind.Artwork,
        "excl" => SyncEntityKind.Exclusion,
        "set" => SyncEntityKind.Setting,
        _ => SyncEntityKind.Game
    };

    private static AccountPush BuildPush(LocalSyncSnapshot local, Dictionary<SyncEntityKind, SyncPlan> plans)
    {
        var upserts = new AccountSnapshot(
            Pick(local.Games, plans[SyncEntityKind.Game]),
            Pick(local.Executables, plans[SyncEntityKind.Executable]),
            Pick(local.Tags, plans[SyncEntityKind.Tag]),
            Pick(local.Sessions, plans[SyncEntityKind.Session]),
            Pick(local.Exclusions, plans[SyncEntityKind.Exclusion]),
            Pick(local.Settings, plans[SyncEntityKind.Setting]),
            Pick(local.Artworks, plans[SyncEntityKind.Artwork]),
            local.Profile);

        var deletions = plans.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value.TombstoneRemotely
                .Select(tombstone => tombstone.Identity).ToList());

        return new AccountPush(upserts, deletions);
    }

    private static IReadOnlyList<T> Pick<T>(LocalSyncSet<T> set, SyncPlan plan) =>
        plan.Upload
            .Select(entry => set.ByIdentity.GetValueOrDefault(entry.Identity))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToList();

    private async Task<AccountApply> BuildApplyAsync(
        LocalSyncSnapshot local,
        AccountSnapshot remote,
        Dictionary<SyncEntityKind, SyncPlan> plans,
        AccountPush push,
        CancellationToken cancellationToken)
    {
        var artworkToApply = Take(remote.Artworks, plans[SyncEntityKind.Artwork], item => item.Identity);

        // Bilddaten liegen im Storage, nicht in der Tabelle - sie werden erst
        // geholt, wenn wirklich ein Cover uebernommen wird.
        var artworkWithData = artworkToApply.Count == 0
            ? artworkToApply
            : await syncClient.DownloadArtworkAsync(artworkToApply, cancellationToken);

        var upserts = new AccountSnapshot(
            Take(remote.Games, plans[SyncEntityKind.Game], item => item.Identity),
            Take(remote.Executables, plans[SyncEntityKind.Executable], item => item.Identity),
            Take(remote.Tags, plans[SyncEntityKind.Tag], item => item.Identity),
            Take(remote.Sessions, plans[SyncEntityKind.Session], item => item.Identity),
            Take(remote.Exclusions, plans[SyncEntityKind.Exclusion], item => item.Identity),
            Take(remote.Settings, plans[SyncEntityKind.Setting], item => item.Identity),
            artworkWithData,
            remote.Profile);

        var removals = plans.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value.DeleteLocally
                .Select(item => item.Identity).ToList());

        // Vermerkt wird dreierlei: was hochgeladen wurde (dann gilt der lokale
        // Hash als abgeglichen), was uebernommen wurde (dann der Hash aus dem
        // Konto) - und was auf beiden Seiten unveraendert ist.
        //
        // Der dritte Fall ist der unscheinbare, aber noetige: ohne ihn behielte ein
        // Datensatz, an dem sich nie etwas aendert, dauerhaft keine gespeicherte
        // Identitaet. Beim Loeschen gaebe es dann nichts, worauf ein Grabstein
        // zeigen koennte, und die Loeschung erreichte das Konto nie.
        var stamps = new Dictionary<SyncEntityKind, IReadOnlyDictionary<string, SyncStamp>>();
        var remoteByKind = RemoteIdentityIndex(remote);

        foreach (var (kind, plan) in plans)
        {
            var kindStamps = new Dictionary<string, SyncStamp>(StringComparer.Ordinal);

            foreach (var entry in LocalEntries(local, kind))
            {
                if (remoteByKind.TryGetValue(kind, out var remoteEntries) &&
                    remoteEntries.TryGetValue(entry.Identity, out var remoteHash))
                {
                    kindStamps[entry.Identity] = new SyncStamp(entry.CloudId, remoteHash);
                }
            }

            foreach (var entry in plan.Upload)
            {
                kindStamps[entry.Identity] = new SyncStamp(entry.CloudId, entry.ContentHash);
            }

            foreach (var entry in plan.ApplyLocally)
            {
                kindStamps[entry.Identity] = new SyncStamp(entry.CloudId, entry.ContentHash);
            }

            stamps[kind] = kindStamps;
        }

        var resolvedTombstones = plans.SelectMany(entry => entry.Value.TombstoneRemotely).ToList();

        return new AccountApply(upserts, removals, stamps, resolvedTombstones, deviceIdentity.MachineKey);
    }

    private static Dictionary<SyncEntityKind, Dictionary<string, string>> RemoteIdentityIndex(AccountSnapshot remote)
    {
        static Dictionary<string, string> Index<T>(
            IReadOnlyList<T> items, Func<T, string> identity, Func<T, string> hash, Func<T, DateTimeOffset?> deleted)
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items.Where(entry => deleted(entry) is null))
            {
                index[identity(item)] = hash(item);
            }

            return index;
        }

        return new Dictionary<SyncEntityKind, Dictionary<string, string>>
        {
            [SyncEntityKind.Game] = Index(remote.Games, item => item.Identity, item => item.ContentHash, item => item.DeletedAt),
            [SyncEntityKind.Executable] = Index(remote.Executables, item => item.Identity, item => item.ContentHash, item => item.DeletedAt),
            [SyncEntityKind.Tag] = Index(remote.Tags, item => item.Identity, item => item.ContentHash, item => item.DeletedAt),
            [SyncEntityKind.Session] = Index(remote.Sessions, item => item.Identity, item => item.ContentHash, item => item.DeletedAt),
            [SyncEntityKind.Exclusion] = Index(remote.Exclusions, item => item.Identity, item => item.ContentHash, item => item.DeletedAt),
            [SyncEntityKind.Setting] = Index(remote.Settings, item => item.Identity, item => item.ContentHash, item => item.DeletedAt),
            [SyncEntityKind.Artwork] = Index(remote.Artworks, item => item.Identity, item => item.ContentHash, item => item.DeletedAt)
        };
    }

    private static IReadOnlyList<LocalSyncEntry> LocalEntries(LocalSyncSnapshot local, SyncEntityKind kind) => kind switch
    {
        SyncEntityKind.Game => local.Games.Entries,
        SyncEntityKind.Executable => local.Executables.Entries,
        SyncEntityKind.Tag => local.Tags.Entries,
        SyncEntityKind.Session => local.Sessions.Entries,
        SyncEntityKind.Exclusion => local.Exclusions.Entries,
        SyncEntityKind.Setting => local.Settings.Entries,
        SyncEntityKind.Artwork => local.Artworks.Entries,
        _ => []
    };

    private static IReadOnlyList<T> Take<T>(
        IReadOnlyList<T> items,
        SyncPlan plan,
        Func<T, string> identity)
    {
        var wanted = plan.ApplyLocally.Select(entry => entry.Identity).ToHashSet(StringComparer.Ordinal);
        return items.Where(item => wanted.Contains(identity(item))).ToList();
    }
}
