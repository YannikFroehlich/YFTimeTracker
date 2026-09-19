using System.Text.Json.Serialization;

namespace YFTimeTracker.Core.Models;

public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Inhalts-Hash zum Zeitpunkt des letzten erfolgreichen Abgleichs. Der
    /// Schluessel ist zugleich die Identitaet, deshalb braucht es hier keine
    /// eigene Cloud-Id.
    /// </summary>
    [JsonIgnore]
    public string? SyncedHash { get; set; }
}
