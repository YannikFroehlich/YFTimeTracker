namespace YFTimeTracker.Core.Models;

using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;

public sealed class Game
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public GameSource Source { get; set; } = GameSource.Manual;

    public string? ExternalGameId { get; set; }

    public string? InstallDirectory { get; set; }

    public string? InstallDirectoryKey { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }

    public int? DailyPlaytimeLimitMinutes { get; set; }

    public int? WeeklyPlaytimeLimitMinutes { get; set; }

    /// <summary>
    /// Bereits vor der Aufzeichnung gespielte Zeit in Minuten.
    ///
    /// Zaehlt in der Gesamtspielzeit mit, aber bewusst in keiner
    /// Zeitraum-Auswertung: der Wert hat kein Datum und wuerde sonst einen
    /// beliebigen Tag oder Monat verfaelschen.
    /// </summary>
    public int? BaselinePlaytimeMinutes { get; set; }

    public bool IsPinned { get; set; }

    public List<GameTag> Tags { get; set; } = [];

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

    [JsonIgnore]
    public string LegacyExecutablePath { get; set; } = string.Empty;

    [JsonIgnore]
    public string LegacyExecutablePathKey { get; set; } = string.Empty;

    [JsonIgnore]
    public string LegacyExecutableName { get; set; } = string.Empty;

    public List<GameExecutable> Executables { get; set; } = [];

    [NotMapped, JsonIgnore]
    public string ExecutablePath
    {
        get => PrimaryExecutable?.ExecutablePath ?? string.Empty;
        set => EnsurePrimaryExecutable().ExecutablePath = value;
    }

    [NotMapped, JsonIgnore]
    public string ExecutablePathKey
    {
        get => PrimaryExecutable?.ExecutablePathKey ?? string.Empty;
        set => EnsurePrimaryExecutable().ExecutablePathKey = value;
    }

    [NotMapped, JsonIgnore]
    public string ExecutableName
    {
        get => PrimaryExecutable?.ExecutableName ?? string.Empty;
        set => EnsurePrimaryExecutable().ExecutableName = value;
    }

    [JsonIgnore]
    public GameExecutable? PrimaryExecutable => Executables.FirstOrDefault(executable => executable.IsPrimary)
        ?? Executables.FirstOrDefault();

    private GameExecutable EnsurePrimaryExecutable()
    {
        if (PrimaryExecutable is { } executable)
        {
            executable.IsPrimary = true;
            return executable;
        }

        executable = new GameExecutable { IsPrimary = true, AddedAtUtc = AddedAtUtc };
        Executables.Add(executable);
        return executable;
    }
}
