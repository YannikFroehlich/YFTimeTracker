using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud;

/// <summary>
/// Transport gegen PostgREST und Supabase Storage fuer den Kontoabgleich.
///
/// Geschrieben wird per Upsert auf (user_id, identity) - die geraeteunabhaengige
/// Identitaet ist der Schluessel, nicht eine Zeilennummer. Geloescht wird nie
/// wirklich, sondern nur <c>deleted_at</c> gesetzt: verschwaende die Zeile,
/// koennte der zweite PC nicht unterscheiden, ob sie geloescht wurde oder dort
/// noch nie ankam, und luede sie beim naechsten Abgleich wieder hoch.
/// </summary>
public sealed partial class SupabaseAccountClient(
    HttpClient httpClient,
    ICloudConnectionProvider connectionProvider,
    ICloudAuthService authService,
    IDeviceIdentityProvider deviceIdentity,
    IClock clock,
    ILogger<SupabaseAccountClient>? logger = null) : IAccountSyncClient
{
    private const string ArtworkBucket = "game-artwork";
    private const int WriteChunkSize = 500;
    private const int ReadPageSize = 1000;

    private readonly ILogger<SupabaseAccountClient> log = logger ?? NullLogger<SupabaseAccountClient>.Instance;

    // -------------------------------------------------------------------------
    // Lesen
    // -------------------------------------------------------------------------

    public async Task<AccountSnapshot> FetchAsync(
        IProgress<CloudProgress>? progress,
        CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(cancellationToken);
        var filter = $"user_id=eq.{context.UserId}";

        var games = await SelectAllAsync<GameRow>(context, "games", filter, cancellationToken);
        var executables = await SelectAllAsync<ExecutableRow>(context, "game_executables", filter, cancellationToken);
        var tags = await SelectAllAsync<TagRow>(context, "game_tags", filter, cancellationToken);
        var sessions = await SelectAllAsync<SessionRow>(context, "game_sessions", filter, cancellationToken);
        var exclusions = await SelectAllAsync<ExclusionRow>(context, "tracking_exclusions", filter, cancellationToken);
        var settings = await SelectAllAsync<SettingRow>(context, "app_settings", filter, cancellationToken);
        var artworks = await SelectAllAsync<ArtworkRow>(context, "game_artwork", filter, cancellationToken);
        var profiles = await SelectAsync<ProfileRow>(context, "profiles", filter, cancellationToken);

        progress?.Report(new CloudProgress("Konto gelesen", 1, 1));

        // Ein geloeschtes Spiel zieht seine EXE-Dateien, Tags, Sessions und Cover
        // mit. In der Datenbank bleiben deren Zeilen stehen - lokal gibt es das
        // Spiel aber nicht mehr, sie waeren also bei jedem Abgleich erneut "neu"
        // und wuerden doch jedes Mal verworfen.
        var deletedGames = games
            .Where(row => row.DeletedAt is not null)
            .Select(row => row.Identity)
            .ToHashSet(StringComparer.Ordinal);

        var now = clock.UtcNow;

        CloudExecutable WithParent(ExecutableRow row) =>
            deletedGames.Contains(row.GameIdentity) ? row.ToModel() with { DeletedAt = now } : row.ToModel();

        CloudTag WithParentTag(TagRow row) =>
            deletedGames.Contains(row.GameIdentity) ? row.ToModel() with { DeletedAt = now } : row.ToModel();

        CloudGameSession WithParentSession(SessionRow row) =>
            deletedGames.Contains(row.GameIdentity) ? row.ToModel() with { DeletedAt = now } : row.ToModel();

        CloudArtwork WithParentArtwork(ArtworkRow row) =>
            deletedGames.Contains(row.GameIdentity) ? row.ToModel() with { DeletedAt = now } : row.ToModel();

        return new AccountSnapshot(
            games.Select(row => row.ToModel()).ToList(),
            executables.Select(WithParent).ToList(),
            tags.Select(WithParentTag).ToList(),
            sessions.Select(WithParentSession).ToList(),
            exclusions.Select(row => row.ToModel()).ToList(),
            settings.Select(row => row.ToModel()).ToList(),
            artworks.Select(WithParentArtwork).ToList(),
            profiles.FirstOrDefault()?.ToModel());
    }

    public async Task<IReadOnlyList<CloudArtwork>> DownloadArtworkAsync(
        IReadOnlyList<CloudArtwork> artworks,
        CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(cancellationToken);
        var result = new List<CloudArtwork>(artworks.Count);

        foreach (var artwork in artworks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var message = new HttpRequestMessage(
                HttpMethod.Get, $"{context.BaseUrl}/storage/v1/object/{ArtworkBucket}/{artwork.StoragePath}");
            ApplyAuthHeaders(message, context);

            using var response = await httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Ein fehlendes Cover darf die Spielzeiten nicht aufhalten; das Bild
                // ist nachrangig und laesst sich jederzeit neu setzen.
                log.LogWarning(
                    "Cover {Path} konnte nicht geladen werden (Status {Status}); der Abgleich läuft ohne dieses Bild weiter",
                    artwork.StoragePath, (int)response.StatusCode);
                continue;
            }

            result.Add(artwork with { ImageData = await response.Content.ReadAsByteArrayAsync(cancellationToken) });
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Schreiben
    // -------------------------------------------------------------------------

    public async Task PushAsync(
        AccountPush push,
        IProgress<CloudProgress>? progress,
        CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(cancellationToken);
        var deviceId = await EnsureDeviceAsync(context, cancellationToken);

        // Reihenfolge ist Absicht: Spiele zuerst, damit untergeordnete Zeilen auf
        // eine vorhandene Spielidentitaet zeigen.
        await UpsertAsync(context, "games", push.Upserts.Games.Select(item => GameRow.From(item, context.UserId)), cancellationToken);
        await UpsertAsync(context, "game_executables", push.Upserts.Executables.Select(item => ExecutableRow.From(item, context.UserId, deviceId)), cancellationToken);
        await UpsertAsync(context, "game_tags", push.Upserts.Tags.Select(item => TagRow.From(item, context.UserId)), cancellationToken);
        await UpsertAsync(context, "game_sessions", push.Upserts.Sessions.Select(item => SessionRow.From(item, context.UserId, deviceId)), cancellationToken);
        await UpsertAsync(context, "tracking_exclusions", push.Upserts.Exclusions.Select(item => ExclusionRow.From(item, context.UserId)), cancellationToken);
        await UpsertAsync(context, "app_settings", push.Upserts.Settings.Select(item => SettingRow.From(item, context.UserId)), cancellationToken);

        if (push.Upserts.Artworks.Count > 0)
        {
            progress?.Report(new CloudProgress("Cover werden übertragen", 0, push.Upserts.Artworks.Count));
            await UploadArtworkAsync(context, push.Upserts.Artworks, progress, cancellationToken);
        }

        if (push.Upserts.Profile is { } profile)
        {
            // Das Profil ist die Ausnahme von der Regel: genau eine Zeile je Konto,
            // eindeutig ueber user_id. Eine "identity"-Spalte gibt es dort nicht.
            await UpsertChunkedAsync(
                context, "profiles", "user_id", [ProfileRow.From(profile, context.UserId)], cancellationToken);
        }

        await MarkDeletedAsync(context, push.DeletedIdentities, cancellationToken);
    }

    private async Task UploadArtworkAsync(
        RequestContext context,
        IReadOnlyList<CloudArtwork> artworks,
        IProgress<CloudProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rows = new List<ArtworkRow>(artworks.Count);
        var done = 0;

        foreach (var artwork in artworks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = BuildArtworkPath(context.UserId, artwork);
            if (artwork.ImageData is { Length: > 0 })
            {
                using var message = new HttpRequestMessage(
                    HttpMethod.Post, $"{context.BaseUrl}/storage/v1/object/{ArtworkBucket}/{path}")
                {
                    Content = new ByteArrayContent(artwork.ImageData)
                };
                message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                    string.IsNullOrWhiteSpace(artwork.ContentType) ? "application/octet-stream" : artwork.ContentType);
                message.Headers.TryAddWithoutValidation("x-upsert", "true");
                ApplyAuthHeaders(message, context);

                using var response = await httpClient.SendAsync(message, cancellationToken);
                await EnsureSuccessAsync(response, $"Cover {path}", cancellationToken);
            }

            rows.Add(ArtworkRow.From(artwork with { StoragePath = path }, context.UserId));
            progress?.Report(new CloudProgress("Cover werden übertragen", ++done, artworks.Count));
        }

        await UpsertAsync(context, "game_artwork", rows, cancellationToken);
    }

    /// <summary>
    /// Die Identitaet enthaelt Zeichen, die sich nicht als Dateiname eignen
    /// (Doppelpunkte, Leerzeichen). Ein Hash davon ist eindeutig und auf jedem
    /// Geraet identisch berechenbar.
    /// </summary>
    private static string BuildArtworkPath(string userId, CloudArtwork artwork)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(artwork.Identity)))[..32];
        var extension = artwork.FileExtension.StartsWith('.') ? artwork.FileExtension : "." + artwork.FileExtension;
        return $"{userId}/{hash}{extension}";
    }

    private async Task MarkDeletedAsync(
        RequestContext context,
        IReadOnlyDictionary<SyncEntityKind, IReadOnlyList<string>> deletions,
        CancellationToken cancellationToken)
    {
        foreach (var (kind, identities) in deletions)
        {
            if (identities.Count == 0)
            {
                continue;
            }

            var table = TableFor(kind);
            foreach (var chunk in identities.Chunk(100))
            {
                var list = string.Join(',', chunk.Select(identity => $"\"{identity.Replace("\"", "\\\"")}\""));
                var filter = $"user_id=eq.{context.UserId}&identity=in.({list})";
                await PatchAsync(
                    context, table, filter,
                    new Dictionary<string, object?> { ["deleted_at"] = clock.UtcNow },
                    cancellationToken);
            }
        }
    }

    private static string TableFor(SyncEntityKind kind) => kind switch
    {
        SyncEntityKind.Game => "games",
        SyncEntityKind.Executable => "game_executables",
        SyncEntityKind.Tag => "game_tags",
        SyncEntityKind.Session => "game_sessions",
        SyncEntityKind.Exclusion => "tracking_exclusions",
        SyncEntityKind.Setting => "app_settings",
        SyncEntityKind.Artwork => "game_artwork",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unbekannte Datensatzart.")
    };

    private async Task<string> EnsureDeviceAsync(RequestContext context, CancellationToken cancellationToken)
    {
        var row = new DeviceRow
        {
            UserId = context.UserId,
            MachineKey = deviceIdentity.MachineKey,
            DeviceName = deviceIdentity.DeviceName,
            AppVersion = context.AppVersion,
            LastSeenAt = clock.UtcNow
        };

        var written = await SendChunkAsync<DeviceRow>(
            context, "devices", "user_id,machine_key", [row], returnRepresentation: true, cancellationToken);

        return written.FirstOrDefault()?.Id
            ?? throw new CloudRequestException("Supabase hat keine Geräte-Id zurückgegeben.", HttpStatusCode.InternalServerError);
    }

    // -------------------------------------------------------------------------
    // PostgREST
    // -------------------------------------------------------------------------

    /// <summary>
    /// Upsert fuer die identitaetsbasierten Tabellen. Das Profil hat kein solches
    /// Feld und wird deshalb direkt ueber <see cref="UpsertChunkedAsync"/> mit
    /// eigenem Konfliktziel geschrieben.
    /// </summary>
    private Task UpsertAsync<T>(
        RequestContext context,
        string table,
        IEnumerable<T> rows,
        CancellationToken cancellationToken) =>
        UpsertChunkedAsync(context, table, "user_id,identity", rows, cancellationToken);

    private async Task UpsertChunkedAsync<T>(
        RequestContext context,
        string table,
        string onConflict,
        IEnumerable<T> rows,
        CancellationToken cancellationToken)
    {
        var buffer = new List<T>(WriteChunkSize);
        foreach (var row in rows)
        {
            buffer.Add(row);
            if (buffer.Count < WriteChunkSize)
            {
                continue;
            }

            await SendChunkAsync<T>(context, table, onConflict, buffer, returnRepresentation: false, cancellationToken);
            buffer.Clear();
        }

        if (buffer.Count > 0)
        {
            await SendChunkAsync<T>(context, table, onConflict, buffer, returnRepresentation: false, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<T>> SendChunkAsync<T>(
        RequestContext context,
        string table,
        string onConflict,
        IReadOnlyList<T> chunk,
        bool returnRepresentation,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"{context.BaseUrl}/rest/v1/{table}?on_conflict={onConflict}")
        {
            Content = JsonContent.Create(chunk, options: SupabaseJson.Options)
        };
        ApplyAuthHeaders(message, context);
        message.Headers.TryAddWithoutValidation(
            "Prefer",
            returnRepresentation
                ? "resolution=merge-duplicates,return=representation"
                : "resolution=merge-duplicates,return=minimal");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, table, cancellationToken);

        if (!returnRepresentation)
        {
            return [];
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<List<T>>(body, SupabaseJson.Options) ?? [];
    }

    private async Task<IReadOnlyList<T>> SelectAsync<T>(
        RequestContext context,
        string table,
        string query,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get, $"{context.BaseUrl}/rest/v1/{table}?select=*&{query}");
        ApplyAuthHeaders(message, context);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, table, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<List<T>>(body, SupabaseJson.Options) ?? [];
    }

    /// <summary>
    /// PostgREST deckelt jede Antwort. Ohne Blaettern fehlten bei grossen
    /// Bibliotheken stillschweigend Sessions.
    /// </summary>
    private async Task<IReadOnlyList<T>> SelectAllAsync<T>(
        RequestContext context,
        string table,
        string filter,
        CancellationToken cancellationToken)
    {
        var all = new List<T>();
        var offset = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await SelectAsync<T>(
                context, table, $"{filter}&order=identity.asc&limit={ReadPageSize}&offset={offset}", cancellationToken);

            all.AddRange(page);
            if (page.Count < ReadPageSize)
            {
                return all;
            }

            offset += ReadPageSize;
        }
    }

    private async Task PatchAsync(
        RequestContext context,
        string table,
        string filter,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Patch, $"{context.BaseUrl}/rest/v1/{table}?{filter}")
        {
            Content = JsonContent.Create(values, options: SupabaseJson.Options)
        };
        ApplyAuthHeaders(message, context);
        message.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, table, cancellationToken);
    }

    private static void ApplyAuthHeaders(HttpRequestMessage message, RequestContext context)
    {
        message.Headers.TryAddWithoutValidation("apikey", context.AnonKey);
        message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.AccessToken}");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string what,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new CloudRequestException(
            $"{what}: {SupabaseErrorReader.Describe(response.StatusCode, body)}",
            response.StatusCode);
    }

    private async Task<RequestContext> CreateContextAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException("Für dieses Build ist kein Supabase-Projekt hinterlegt.");

        var accessToken = await authService.GetAccessTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("Für den Kontoabgleich ist eine Anmeldung erforderlich.");

        var userId = authService.CurrentSession?.UserId
            ?? throw new InvalidOperationException("Die angemeldete Sitzung enthält keine Benutzer-Id.");

        var appVersion = typeof(SupabaseAccountClient).Assembly.GetName().Version?.ToString();
        return new RequestContext(connection.NormalizedUrl, connection.AnonKey, accessToken, userId, appVersion);
    }

    private sealed record RequestContext(
        string BaseUrl,
        string AnonKey,
        string AccessToken,
        string UserId,
        string? AppVersion);
}
