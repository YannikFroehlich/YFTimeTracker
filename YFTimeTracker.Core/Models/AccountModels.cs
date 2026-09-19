namespace YFTimeTracker.Core.Models;

// Datensaetze, wie sie im Konto stehen. Bewusst in Core, damit die Data-Schicht
// (liest und schreibt lokal) und die Cloud-Schicht (ueberträgt) dieselbe Sicht
// teilen, ohne voneinander zu wissen.
//
// Jeder Datensatz traegt seine geraeteunabhaengige Identitaet und den
// Inhalts-Hash mit sich; daran haengt der gesamte Abgleich.

public sealed record CloudGame(
    string Identity,
    string? CloudId,
    string ContentHash,
    string Name,
    GameSource Source,
    string? ExternalGameId,
    DateTimeOffset AddedAtUtc,
    int? DailyLimitMinutes,
    int? WeeklyLimitMinutes,
    bool IsPinned,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset UpdatedAt = default);

public sealed record CloudExecutable(
    string Identity,
    string? CloudId,
    string ContentHash,
    string GameIdentity,
    string ExecutablePath,
    string ExecutablePathKey,
    string ExecutableName,
    bool IsPrimary,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset UpdatedAt = default);

public sealed record CloudTag(
    string Identity,
    string? CloudId,
    string ContentHash,
    string GameIdentity,
    string Tag,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset UpdatedAt = default);

public sealed record CloudGameSession(
    string Identity,
    string? CloudId,
    string ContentHash,
    string GameIdentity,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? EndedAtUtc,
    long? DurationSeconds,
    string BootSessionId,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset UpdatedAt = default);

public sealed record CloudExclusion(
    string Identity,
    string? CloudId,
    string ContentHash,
    TrackingExclusionKind Kind,
    string Value,
    string ValueKey,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset UpdatedAt = default);

public sealed record CloudSetting(
    string Identity,
    string? CloudId,
    string ContentHash,
    string Key,
    string Value,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset UpdatedAt = default);

public sealed record CloudArtwork(
    string Identity,
    string? CloudId,
    string ContentHash,
    string GameIdentity,
    string ContentType,
    string FileExtension,
    string Sha256,
    string StoragePath,
    DateTimeOffset UpdatedAtUtc,
    byte[]? ImageData = null,
    DateTimeOffset? DeletedAt = null,
    DateTimeOffset UpdatedAt = default);

/// <summary>Anzeigename und Akzentfarbe, jetzt Teil des Kontos statt nur lokal.</summary>
public sealed record CloudProfile(string? DisplayName, string? AccentColor, DateTimeOffset UpdatedAt);

/// <summary>Der vollstaendige Datenstand eines Kontos beziehungsweise ein Ausschnitt davon.</summary>
public sealed record AccountSnapshot(
    IReadOnlyList<CloudGame> Games,
    IReadOnlyList<CloudExecutable> Executables,
    IReadOnlyList<CloudTag> Tags,
    IReadOnlyList<CloudGameSession> Sessions,
    IReadOnlyList<CloudExclusion> Exclusions,
    IReadOnlyList<CloudSetting> Settings,
    IReadOnlyList<CloudArtwork> Artworks,
    CloudProfile? Profile = null)
{
    public static AccountSnapshot Empty { get; } = new([], [], [], [], [], [], []);

    public int TotalCount =>
        Games.Count + Executables.Count + Tags.Count + Sessions.Count +
        Exclusions.Count + Settings.Count + Artworks.Count;
}

/// <summary>
/// Was hochgeladen und was im Konto als geloescht markiert werden soll.
/// Loeschungen sind nach Datensatzart getrennt, weil sie in unterschiedliche
/// Tabellen gehen.
/// </summary>
public sealed record AccountPush(
    AccountSnapshot Upserts,
    IReadOnlyDictionary<SyncEntityKind, IReadOnlyList<string>> DeletedIdentities);

/// <summary>
/// Was der lokale Datenbestand aus dem Abgleich uebernehmen soll: neue
/// beziehungsweise geaenderte Datensaetze, lokal zu entfernende Identitaeten und
/// die Zuordnung Identitaet -> (Cloud-Id, Hash), die als "abgeglichen" vermerkt
/// wird.
/// </summary>
public sealed record AccountApply(
    AccountSnapshot Upserts,
    IReadOnlyDictionary<SyncEntityKind, IReadOnlyList<string>> RemoveIdentities,
    IReadOnlyDictionary<SyncEntityKind, IReadOnlyDictionary<string, SyncStamp>> Stamps,
    IReadOnlyList<LocalTombstone> ResolvedTombstones,
    string MachineKey);

/// <summary>
/// Was nach einem erfolgreichen Abgleich an einem lokalen Datensatz vermerkt
/// wird. Schluessel des Abgleichs ist durchgaengig die Identitaet; die Cloud-Id
/// wird nur mitgefuehrt, wenn sie bekannt ist, und dient der Fehlersuche.
/// </summary>
public sealed record SyncStamp(string? CloudId, string ContentHash);
