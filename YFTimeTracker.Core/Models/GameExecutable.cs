using System.Text.Json.Serialization;

namespace YFTimeTracker.Core.Models;

public sealed class GameExecutable
{
    public long Id { get; set; }

    public long GameId { get; set; }

    public Game? Game { get; set; }

    public string ExecutablePath { get; set; } = string.Empty;

    public string ExecutablePathKey { get; set; } = string.Empty;

    public string ExecutableName { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }
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
