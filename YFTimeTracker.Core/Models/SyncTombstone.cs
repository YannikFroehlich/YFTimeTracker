namespace YFTimeTracker.Core.Models;

/// <summary>
/// Nachweis, dass ein Datensatz hier geloescht wurde.
///
/// Ohne diesen Nachweis waere beim naechsten Abgleich nicht zu unterscheiden, ob
/// ein Datensatz hier geloescht oder auf dem anderen PC neu angelegt wurde - und
/// jedes geloeschte Spiel kaeme aus dem Konto zurueck.
///
/// Grabsteine werden nach dem erfolgreichen Abgleich wieder entfernt; sie leben
/// nur zwischen dem Loeschen und dem naechsten Hochladen.
/// </summary>
public sealed class SyncTombstone
{
    public long Id { get; set; }

    public SyncEntityKind Kind { get; set; }

    /// <summary>Geraeteunabhaengige Identitaet des geloeschten Datensatzes.</summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>Kennung im Konto, sofern der Datensatz dort schon angekommen war.</summary>
    public string? CloudId { get; set; }

    public DateTimeOffset DeletedAtUtc { get; set; }
}
