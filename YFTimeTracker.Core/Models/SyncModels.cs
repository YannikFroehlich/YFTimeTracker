namespace YFTimeTracker.Core.Models;

/// <summary>Art des Datensatzes, den der Abgleich behandelt.</summary>
public enum SyncEntityKind
{
    Game,
    Executable,
    Tag,
    Session,
    Artwork,
    Exclusion,
    Setting
}

/// <summary>
/// Ein lokaler Datensatz, wie ihn der Abgleich sieht.
///
/// <paramref name="SyncedHash"/> ist der Inhalts-Hash zum Zeitpunkt des letzten
/// erfolgreichen Abgleichs. Daraus ergibt sich ohne zusaetzliche Zeitstempel in
/// jedem Schreibpfad, ob dieser Datensatz seitdem lokal geaendert wurde:
/// <c>null</c> heisst "noch nie abgeglichen", ein abweichender Hash heisst
/// "hier geaendert".
/// </summary>
public sealed record LocalSyncEntry(
    string Identity,
    long LocalId,
    string? CloudId,
    string ContentHash,
    string? SyncedHash)
{
    public bool HasChangedLocally => SyncedHash is null || !string.Equals(ContentHash, SyncedHash, StringComparison.Ordinal);
}

/// <summary>Ein Datensatz, wie er im Konto steht.</summary>
public sealed record RemoteSyncEntry(
    string Identity,
    string CloudId,
    string ContentHash,
    DateTimeOffset UpdatedAtUtc,
    bool IsDeleted);

/// <summary>
/// Ein lokal geloeschter Datensatz. Ohne diesen Nachweis waere "fehlt lokal"
/// nicht von "wurde auf dem anderen Geraet neu angelegt" zu unterscheiden, und
/// jeder Abgleich holte geloeschte Spiele zurueck.
/// </summary>
public sealed record LocalTombstone(string Identity, string? CloudId, DateTimeOffset DeletedAtUtc);

/// <summary>
/// Ein Datensatz, der auf beiden Seiten seit dem letzten Abgleich geaendert
/// wurde. Der Stand aus dem Konto gewinnt; der Eintrag dient dazu, das dem
/// Benutzer und dem Log gegenueber benennen zu koennen statt still zu
/// ueberschreiben.
/// </summary>
public sealed record SyncConflict(SyncEntityKind Kind, string Identity, string? CloudId);

/// <summary>Was der Abgleich fuer eine Datensatzart zu tun hat.</summary>
public sealed record SyncPlan(
    SyncEntityKind Kind,
    IReadOnlyList<LocalSyncEntry> Upload,
    IReadOnlyList<RemoteSyncEntry> ApplyLocally,
    IReadOnlyList<RemoteSyncEntry> DeleteLocally,
    IReadOnlyList<LocalTombstone> TombstoneRemotely,
    IReadOnlyList<SyncConflict> Conflicts)
{
    public bool IsEmpty =>
        Upload.Count == 0 &&
        ApplyLocally.Count == 0 &&
        DeleteLocally.Count == 0 &&
        TombstoneRemotely.Count == 0;

    public static SyncPlan Empty(SyncEntityKind kind) => new(kind, [], [], [], [], []);
}

public sealed record SyncSummary(
    int Uploaded,
    int Downloaded,
    int DeletedLocally,
    int DeletedRemotely,
    IReadOnlyList<SyncConflict> Conflicts,
    DateTimeOffset CompletedAtUtc);
