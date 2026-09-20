using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Core.Abstractions;

/// <summary>
/// Sicherer Ablageort fuer Zugangsdaten. Die Windows-Schicht setzt das auf den
/// Windows-Anmeldeinformationsspeicher um; Tests koennen es im Speicher halten.
/// </summary>
public interface ISecretStore
{
    string? Read(string name);

    void Write(string name, string secret);

    void Delete(string name);
}

/// <summary>
/// Liefert Projekt-URL und Publishable Key des Supabase-Projekts.
///
/// Im ausgelieferten Build kommen die Werte aus einer mitgelieferten
/// Konfigurationsdatei, damit sich Benutzer schlicht anmelden koennen, statt
/// erst ein eigenes Projekt einzurichten.
/// </summary>
public interface ICloudConnectionProvider
{
    Task<CloudConnectionSettings?> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Stabile Kennung dieses Rechners, damit Sessions ihrem Geraet zuordenbar bleiben.</summary>
public interface IDeviceIdentityProvider
{
    string MachineKey { get; }

    string DeviceName { get; }
}

/// <summary>Anmeldung gegen Supabase Auth (GoTrue).</summary>
public interface ICloudAuthService
{
    CloudSession? CurrentSession { get; }

    bool IsSignedIn { get; }

    event EventHandler? SessionChanged;

    Task<CloudAuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken);

    Task<CloudAuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>Stellt eine Sitzung aus dem gespeicherten Refresh-Token wieder her.</summary>
    Task<CloudAuthResult> RestoreSessionAsync(CancellationToken cancellationToken);

    /// <summary>Gibt einen gueltigen Access-Token zurueck und erneuert ihn bei Bedarf.</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Der lokale Datenbestand aus Sicht des Abgleichs: je Datensatzart die
/// Abgleich-Metadaten und die zugehoerigen Nutzdaten.
/// </summary>
public sealed record LocalSyncSet<T>(
    IReadOnlyList<LocalSyncEntry> Entries,
    IReadOnlyDictionary<string, T> ByIdentity)
{
    public static LocalSyncSet<T> Empty { get; } = new([], new Dictionary<string, T>());
}

public sealed record LocalSyncSnapshot(
    LocalSyncSet<CloudGame> Games,
    LocalSyncSet<CloudExecutable> Executables,
    LocalSyncSet<CloudTag> Tags,
    LocalSyncSet<CloudGameSession> Sessions,
    LocalSyncSet<CloudExclusion> Exclusions,
    LocalSyncSet<CloudSetting> Settings,
    LocalSyncSet<CloudArtwork> Artworks,
    IReadOnlyList<LocalTombstone> Tombstones,
    CloudProfile Profile);

/// <summary>Liest den lokalen Datenbestand fuer den Abgleich und schreibt das Ergebnis zurueck.</summary>
public interface IAccountSyncStore
{
    Task<LocalSyncSnapshot> ReadAsync(string machineKey, CancellationToken cancellationToken);

    /// <summary>
    /// Uebernimmt Aenderungen aus dem Konto, entfernt dort geloeschte Datensaetze
    /// und vermerkt den erreichten Abgleichstand. Laeuft in einer Transaktion:
    /// ein Abbruch darf keinen halb uebernommenen Stand hinterlassen.
    /// </summary>
    Task ApplyAsync(AccountApply apply, CancellationToken cancellationToken);
}

/// <summary>Transport gegen Supabase. Kennt keine lokale Datenbank.</summary>
public interface IAccountSyncClient
{
    Task<AccountSnapshot> FetchAsync(IProgress<CloudProgress>? progress, CancellationToken cancellationToken);

    Task PushAsync(AccountPush push, IProgress<CloudProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Die im Konto bekannten Geraete. Wird nur zum Benennen fremder Sessions
    /// gebraucht und ist deshalb vom eigentlichen Abgleich getrennt.
    /// </summary>
    Task<IReadOnlyList<CloudDevice>> FetchDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Laedt die Bilddaten zu bereits bekannten Cover-Metadaten nach.</summary>
    Task<IReadOnlyList<CloudArtwork>> DownloadArtworkAsync(
        IReadOnlyList<CloudArtwork> artworks,
        CancellationToken cancellationToken);
}

/// <summary>
/// Ablage der taeglichen Sicherungsdatei im Konto. Namen sind reine Dateinamen;
/// vorhandene Dateien werden nie ueberschrieben, damit ein zweiter PC die
/// Sicherung desselben Tages nicht ersetzen kann.
/// </summary>
public interface ICloudBackupClient
{
    Task UploadAsync(string name, byte[] content, CancellationToken cancellationToken);

    Task<IReadOnlyList<CloudBackupFile>> ListAsync(CancellationToken cancellationToken);

    Task<byte[]> DownloadAsync(string name, CancellationToken cancellationToken);

    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Der Abgleich zwischen lokalem Bestand und Konto.</summary>
public interface IAccountSyncService
{
    /// <summary>Wahr, sobald eine gueltige Anmeldung vorliegt.</summary>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);

    Task<SyncSummary?> SyncNowAsync(IProgress<CloudProgress>? progress, CancellationToken cancellationToken);

    Task<DateTimeOffset?> GetLastSyncAtUtcAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gleicht im Hintergrund ab, ohne zu werfen. Fuer den App-Start und den
    /// regelmaessigen Lauf: ein nicht erreichbares Supabase darf weder den Start
    /// noch das Tracking stoeren.
    /// </summary>
    Task TrySyncAsync(CancellationToken cancellationToken);

    event EventHandler<SyncSummary>? SyncCompleted;
}
