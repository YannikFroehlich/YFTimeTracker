using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Services;

/// <summary>
/// Entscheidet je Datensatz, in welche Richtung abgeglichen wird.
///
/// Bewusst reine Mengenlogik ohne Netz, Datenbank oder Uhr: der Abgleich ist der
/// Teil der Cloud-Funktion, bei dem ein Denkfehler stillschweigend Spielzeit
/// verliert oder geloeschte Spiele zurueckholt. Genau deshalb muss er sich ohne
/// Supabase durchtesten lassen.
///
/// Die Regeln in Worten:
/// - Lokal geloescht gewinnt immer. Wer hier loescht, meint es.
/// - Im Konto geloescht entfernt den Datensatz auch hier.
/// - Nur auf einer Seite vorhanden: dorthin uebertragen, wo er fehlt.
/// - Auf beiden Seiten geaendert: der Stand aus dem Konto gewinnt, und der Fall
///   wird als Konflikt gemeldet statt still ueberschrieben zu werden.
/// </summary>
public static class SyncPlanner
{
    public static SyncPlan Plan(
        SyncEntityKind kind,
        IReadOnlyCollection<LocalSyncEntry> local,
        IReadOnlyCollection<RemoteSyncEntry> remote,
        IReadOnlyCollection<LocalTombstone> localTombstones)
    {
        var localByIdentity = BuildIndex(local, entry => entry.Identity);
        var remoteByIdentity = BuildIndex(remote, entry => entry.Identity);
        var tombstonesByIdentity = BuildIndex(localTombstones, entry => entry.Identity);

        var upload = new List<LocalSyncEntry>();
        var applyLocally = new List<RemoteSyncEntry>();
        var deleteLocally = new List<RemoteSyncEntry>();
        var tombstoneRemotely = new List<LocalTombstone>();
        var conflicts = new List<SyncConflict>();

        foreach (var identity in AllIdentities(localByIdentity, remoteByIdentity, tombstonesByIdentity))
        {
            var localEntry = localByIdentity.GetValueOrDefault(identity);
            var remoteEntry = remoteByIdentity.GetValueOrDefault(identity);

            // Ein lokaler Grabstein schlaegt alles. Haette der Datensatz lokal
            // ueberlebt (neu angelegt nach dem Loeschen), gaebe es keinen
            // Grabstein mehr - der wird beim Anlegen entfernt.
            if (tombstonesByIdentity.TryGetValue(identity, out var tombstone) && localEntry is null)
            {
                if (remoteEntry is { IsDeleted: false })
                {
                    tombstoneRemotely.Add(tombstone);
                }

                continue;
            }

            if (remoteEntry is { IsDeleted: true })
            {
                if (localEntry is not null)
                {
                    deleteLocally.Add(remoteEntry);
                }

                continue;
            }

            if (localEntry is not null && remoteEntry is null)
            {
                upload.Add(localEntry);
                continue;
            }

            if (localEntry is null && remoteEntry is not null)
            {
                applyLocally.Add(remoteEntry);
                continue;
            }

            if (localEntry is null || remoteEntry is null)
            {
                continue;
            }

            var changedLocally = localEntry.HasChangedLocally;
            var changedRemotely = !string.Equals(remoteEntry.ContentHash, localEntry.SyncedHash, StringComparison.Ordinal);

            if (!changedLocally && !changedRemotely)
            {
                continue;
            }

            if (changedLocally && !changedRemotely)
            {
                upload.Add(localEntry);
                continue;
            }

            if (!changedLocally && changedRemotely)
            {
                applyLocally.Add(remoteEntry);
                continue;
            }

            // Beide Seiten geaendert. Gleicher Inhalt trotz getrennter Bearbeitung
            // ist kein Konflikt, sondern schlicht schon einig.
            if (string.Equals(remoteEntry.ContentHash, localEntry.ContentHash, StringComparison.Ordinal))
            {
                continue;
            }

            applyLocally.Add(remoteEntry);
            conflicts.Add(new SyncConflict(kind, identity, remoteEntry.CloudId));
        }

        return new SyncPlan(kind, upload, applyLocally, deleteLocally, tombstoneRemotely, conflicts);
    }

    private static Dictionary<string, T> BuildIndex<T>(IReadOnlyCollection<T> items, Func<T, string> identitySelector)
    {
        var index = new Dictionary<string, T>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            // Doppelte Identitaeten koennen entstehen, wenn zwei manuell angelegte
            // Spiele denselben Namen tragen. Der erste gewinnt, damit der Abgleich
            // bei jedem Lauf dieselbe Entscheidung trifft.
            index.TryAdd(identitySelector(item), item);
        }

        return index;
    }

    private static IEnumerable<string> AllIdentities<TLocal, TRemote, TTombstone>(
        Dictionary<string, TLocal> local,
        Dictionary<string, TRemote> remote,
        Dictionary<string, TTombstone> tombstones)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identity in local.Keys.Concat(remote.Keys).Concat(tombstones.Keys))
        {
            if (seen.Add(identity))
            {
                yield return identity;
            }
        }
    }
}
