using System.Text.Json.Serialization;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud;

// Abbild der Tabellen aus supabase/schema.sql (Schemaversion 3). Spaltennamen
// sind snake_case wie in Postgres; PostgREST liefert und erwartet sie so.
//
// "id" und "updated_at" werden beim Schreiben bewusst weggelassen: die Id
// vergibt die Datenbank, und updated_at setzt ein Trigger mit der Serverzeit -
// eine falsch gehende Uhr auf einem PC soll die Reihenfolge der Aenderungen
// nicht verfaelschen.

internal sealed record DeviceRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("machine_key")] public string MachineKey { get; init; } = string.Empty;

    [JsonPropertyName("device_name")] public string DeviceName { get; init; } = string.Empty;

    [JsonPropertyName("app_version")] public string? AppVersion { get; init; }

    [JsonPropertyName("last_seen_at")] public DateTimeOffset LastSeenAt { get; init; }
}

internal sealed record ProfileRow
{
    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("display_name")] public string? DisplayName { get; init; }

    [JsonPropertyName("accent_color")] public string? AccentColor { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public CloudProfile ToModel() => new(DisplayName, AccentColor, UpdatedAt);

    public static ProfileRow From(CloudProfile profile, string userId) => new()
    {
        UserId = userId,
        DisplayName = profile.DisplayName,
        AccentColor = profile.AccentColor
    };
}

internal sealed record GameRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("identity")] public string Identity { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("source")] public int Source { get; init; }

    [JsonPropertyName("external_game_id")] public string? ExternalGameId { get; init; }

    [JsonPropertyName("added_at_utc")] public DateTimeOffset AddedAtUtc { get; init; }

    [JsonPropertyName("daily_limit_minutes")] public int? DailyLimitMinutes { get; init; }

    [JsonPropertyName("weekly_limit_minutes")] public int? WeeklyLimitMinutes { get; init; }

    [JsonPropertyName("is_pinned")] public bool IsPinned { get; init; }

    [JsonPropertyName("baseline_minutes")] public int? BaselineMinutes { get; init; }

    // Muss auch als null uebertragen werden: beim erneuten Hochladen hebt genau
    // dieser Wert einen frueheren Grabstein im Konto wieder auf. Die globalen
    // JSON-Optionen lassen Nullwerte sonst weg.
    [JsonPropertyName("deleted_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public CloudGame ToModel() => new(
        Identity, Id, ContentHash, Name, (GameSource)Source, ExternalGameId,
        AddedAtUtc, DailyLimitMinutes, WeeklyLimitMinutes, IsPinned, BaselineMinutes, DeletedAt, UpdatedAt);

    public static GameRow From(CloudGame game, string userId) => new()
    {
        UserId = userId,
        Identity = game.Identity,
        ContentHash = game.ContentHash,
        Name = game.Name,
        Source = (int)game.Source,
        ExternalGameId = game.ExternalGameId,
        AddedAtUtc = game.AddedAtUtc,
        DailyLimitMinutes = game.DailyLimitMinutes,
        WeeklyLimitMinutes = game.WeeklyLimitMinutes,
        IsPinned = game.IsPinned,
        BaselineMinutes = game.BaselineMinutes,

        // Ein Datensatz, der gerade hochgeladen wird, existiert wieder - ein
        // frueherer Grabstein im Konto muss dabei aufgehoben werden.
        DeletedAt = null
    };
}

internal sealed record ExecutableRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("identity")] public string Identity { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("game_identity")] public string GameIdentity { get; init; } = string.Empty;

    [JsonPropertyName("device_id")] public string? DeviceId { get; init; }

    [JsonPropertyName("executable_path")] public string ExecutablePath { get; init; } = string.Empty;

    [JsonPropertyName("executable_path_key")] public string ExecutablePathKey { get; init; } = string.Empty;

    [JsonPropertyName("executable_name")] public string ExecutableName { get; init; } = string.Empty;

    [JsonPropertyName("is_primary")] public bool IsPrimary { get; init; }

    [JsonPropertyName("added_at_utc")] public DateTimeOffset AddedAtUtc { get; init; }

    // Muss auch als null uebertragen werden: beim erneuten Hochladen hebt genau
    // dieser Wert einen frueheren Grabstein im Konto wieder auf. Die globalen
    // JSON-Optionen lassen Nullwerte sonst weg.
    [JsonPropertyName("deleted_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public CloudExecutable ToModel() => new(
        Identity, Id, ContentHash, GameIdentity, ExecutablePath, ExecutablePathKey,
        ExecutableName, IsPrimary, AddedAtUtc, DeletedAt, UpdatedAt);

    public static ExecutableRow From(CloudExecutable item, string userId, string deviceId) => new()
    {
        UserId = userId,
        Identity = item.Identity,
        ContentHash = item.ContentHash,
        GameIdentity = item.GameIdentity,
        DeviceId = deviceId,
        ExecutablePath = item.ExecutablePath,
        ExecutablePathKey = item.ExecutablePathKey,
        ExecutableName = item.ExecutableName,
        IsPrimary = item.IsPrimary,
        AddedAtUtc = item.AddedAtUtc,
        DeletedAt = null
    };
}

internal sealed record TagRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("identity")] public string Identity { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("game_identity")] public string GameIdentity { get; init; } = string.Empty;

    [JsonPropertyName("tag")] public string Tag { get; init; } = string.Empty;

    // Muss auch als null uebertragen werden: beim erneuten Hochladen hebt genau
    // dieser Wert einen frueheren Grabstein im Konto wieder auf. Die globalen
    // JSON-Optionen lassen Nullwerte sonst weg.
    [JsonPropertyName("deleted_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public CloudTag ToModel() => new(Identity, Id, ContentHash, GameIdentity, Tag, DeletedAt, UpdatedAt);

    public static TagRow From(CloudTag item, string userId) => new()
    {
        UserId = userId,
        Identity = item.Identity,
        ContentHash = item.ContentHash,
        GameIdentity = item.GameIdentity,
        Tag = item.Tag,
        DeletedAt = null
    };
}

internal sealed record SessionRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("identity")] public string Identity { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("game_identity")] public string GameIdentity { get; init; } = string.Empty;

    [JsonPropertyName("device_id")] public string? DeviceId { get; init; }

    [JsonPropertyName("started_at_utc")] public DateTimeOffset StartedAtUtc { get; init; }

    [JsonPropertyName("last_seen_at_utc")] public DateTimeOffset LastSeenAtUtc { get; init; }

    [JsonPropertyName("ended_at_utc")] public DateTimeOffset? EndedAtUtc { get; init; }

    [JsonPropertyName("duration_seconds")] public long? DurationSeconds { get; init; }

    [JsonPropertyName("boot_session_id")] public string BootSessionId { get; init; } = string.Empty;

    // Muss auch als null uebertragen werden: beim erneuten Hochladen hebt genau
    // dieser Wert einen frueheren Grabstein im Konto wieder auf. Die globalen
    // JSON-Optionen lassen Nullwerte sonst weg.
    [JsonPropertyName("deleted_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public CloudGameSession ToModel() => new(
        Identity, Id, ContentHash, GameIdentity, StartedAtUtc, LastSeenAtUtc,
        EndedAtUtc, DurationSeconds, BootSessionId, DeletedAt, UpdatedAt);

    public static SessionRow From(CloudGameSession item, string userId, string deviceId) => new()
    {
        UserId = userId,
        Identity = item.Identity,
        ContentHash = item.ContentHash,
        GameIdentity = item.GameIdentity,
        DeviceId = deviceId,
        StartedAtUtc = item.StartedAtUtc,
        LastSeenAtUtc = item.LastSeenAtUtc,
        EndedAtUtc = item.EndedAtUtc,
        DurationSeconds = item.DurationSeconds,
        BootSessionId = item.BootSessionId,
        DeletedAt = null
    };
}

internal sealed record ExclusionRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("identity")] public string Identity { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("kind")] public int Kind { get; init; }

    [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;

    [JsonPropertyName("value_key")] public string ValueKey { get; init; } = string.Empty;

    [JsonPropertyName("added_at_utc")] public DateTimeOffset AddedAtUtc { get; init; }

    // Muss auch als null uebertragen werden: beim erneuten Hochladen hebt genau
    // dieser Wert einen frueheren Grabstein im Konto wieder auf. Die globalen
    // JSON-Optionen lassen Nullwerte sonst weg.
    [JsonPropertyName("deleted_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public CloudExclusion ToModel() => new(
        Identity, Id, ContentHash, (TrackingExclusionKind)Kind, Value, ValueKey, AddedAtUtc, DeletedAt, UpdatedAt);

    public static ExclusionRow From(CloudExclusion item, string userId) => new()
    {
        UserId = userId,
        Identity = item.Identity,
        ContentHash = item.ContentHash,
        Kind = (int)item.Kind,
        Value = item.Value,
        ValueKey = item.ValueKey,
        AddedAtUtc = item.AddedAtUtc,
        DeletedAt = null
    };
}

internal sealed record SettingRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("identity")] public string Identity { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;

    [JsonPropertyName("updated_at_utc")] public DateTimeOffset UpdatedAtUtc { get; init; }

    // Muss auch als null uebertragen werden: beim erneuten Hochladen hebt genau
    // dieser Wert einen frueheren Grabstein im Konto wieder auf. Die globalen
    // JSON-Optionen lassen Nullwerte sonst weg.
    [JsonPropertyName("deleted_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    public CloudSetting ToModel() => new(
        Identity, Id, ContentHash, Key, Value, UpdatedAtUtc, DeletedAt, UpdatedAt);

    public static SettingRow From(CloudSetting item, string userId) => new()
    {
        UserId = userId,
        Identity = item.Identity,
        ContentHash = item.ContentHash,
        Key = item.Key,
        Value = item.Value,
        UpdatedAtUtc = item.UpdatedAtUtc,
        DeletedAt = null
    };
}

internal sealed record ArtworkRow
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("user_id")] public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("identity")] public string Identity { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")] public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("game_identity")] public string GameIdentity { get; init; } = string.Empty;

    [JsonPropertyName("content_type")] public string ContentType { get; init; } = string.Empty;

    [JsonPropertyName("file_extension")] public string FileExtension { get; init; } = string.Empty;

    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("storage_path")] public string StoragePath { get; init; } = string.Empty;

    [JsonPropertyName("updated_at_utc")] public DateTimeOffset UpdatedAtUtc { get; init; }

    // Muss auch als null uebertragen werden: beim erneuten Hochladen hebt genau
    // dieser Wert einen frueheren Grabstein im Konto wieder auf. Die globalen
    // JSON-Optionen lassen Nullwerte sonst weg.
    [JsonPropertyName("deleted_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Die Bilddaten liegen im Storage und werden erst bei Bedarf geholt.</summary>
    public CloudArtwork ToModel() => new(
        Identity, Id, ContentHash, GameIdentity, ContentType, FileExtension,
        Sha256, StoragePath, UpdatedAtUtc, null, DeletedAt, UpdatedAt);

    public static ArtworkRow From(CloudArtwork item, string userId) => new()
    {
        UserId = userId,
        Identity = item.Identity,
        ContentHash = item.ContentHash,
        GameIdentity = item.GameIdentity,
        ContentType = item.ContentType,
        FileExtension = item.FileExtension,
        Sha256 = item.Sha256,
        StoragePath = item.StoragePath,
        UpdatedAtUtc = item.UpdatedAtUtc,
        DeletedAt = null
    };
}
