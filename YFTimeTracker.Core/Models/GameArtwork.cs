using System.Text.Json.Serialization;

namespace YFTimeTracker.Core.Models;

public sealed class GameArtwork
{
    public long GameId { get; set; }

    public Game? Game { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public string FileExtension { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public byte[] ImageData { get; set; } = [];

    public DateTimeOffset UpdatedAtUtc { get; set; }
    /// <summary>
    /// Kennung dieses Datensatzes im Konto. Bleibt <c>null</c>, solange nie
    /// synchronisiert wurde.
    /// </summary>
    [JsonIgnore]
    public string? CloudId { get; set; }
    /// <summary>
    /// Geraeteunabhaengige Identitaet im Konto, sobald der Datensatz einmal
    /// abgeglichen wurde.
    ///
    /// Wird gespeichert statt jedes Mal neu berechnet, weil sie sich sonst
    /// aendern koennte: die Identitaet einer Session enthaelt den MachineKey
    /// ihres Ursprungsgeraets. Auf einem zweiten PC neu berechnet ergaebe sie
    /// einen anderen Wert - und dieselbe Session wuerde ein zweites Mal
    /// hochgeladen.
    /// </summary>
    [JsonIgnore]
    public string? CloudIdentity { get; set; }

    /// <summary>
    /// Inhalts-Hash zum Zeitpunkt des letzten erfolgreichen Abgleichs. Weicht der
    /// aktuelle Hash davon ab, wurde der Datensatz hier geaendert.
    /// </summary>
    [JsonIgnore]
    public string? SyncedHash { get; set; }
}
