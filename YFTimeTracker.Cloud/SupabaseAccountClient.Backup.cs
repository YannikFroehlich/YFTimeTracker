using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud;

/// <summary>
/// Sicherungsdateien im Storage-Bucket <c>backups</c>, Pfadschema
/// <c>{user_id}/{dateiname}</c>. Hochgeladen wird ohne <c>x-upsert</c>: liegt die
/// Tagessicherung schon im Konto, lehnt Supabase das Ueberschreiben ab.
/// </summary>
public sealed partial class SupabaseAccountClient : ICloudBackupClient
{
    private const string BackupBucket = "backups";
    private const int BackupListLimit = 1000;

    public async Task UploadAsync(string name, byte[] content, CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Post, BackupUrl(context, name))
        {
            Content = new ByteArrayContent(content)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        ApplyAuthHeaders(message, context);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, $"Sicherung {name}", cancellationToken);
    }

    public async Task<IReadOnlyList<CloudBackupFile>> ListAsync(CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(cancellationToken);
        using var message = new HttpRequestMessage(
            HttpMethod.Post, $"{context.BaseUrl}/storage/v1/object/list/{BackupBucket}")
        {
            Content = JsonContent.Create(new { prefix = context.UserId, limit = BackupListLimit }, options: SupabaseJson.Options)
        };
        ApplyAuthHeaders(message, context);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, "Sicherungen", cancellationToken);

        var entries = await response.Content.ReadFromJsonAsync<List<StorageEntry>>(SupabaseJson.Options, cancellationToken) ?? [];

        // Ordner stehen ohne Id in der Liste.
        return entries
            .Where(entry => entry.Id is not null && entry.CreatedAt is not null)
            .Select(entry => new CloudBackupFile(entry.Name, entry.CreatedAt!.Value))
            .ToList();
    }

    public async Task<byte[]> DownloadAsync(string name, CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Get, BackupUrl(context, name));
        ApplyAuthHeaders(message, context);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, $"Sicherung {name}", cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteAsync(string name, CancellationToken cancellationToken)
    {
        var context = await CreateContextAsync(cancellationToken);
        using var message = new HttpRequestMessage(HttpMethod.Delete, BackupUrl(context, name));
        ApplyAuthHeaders(message, context);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, $"Sicherung {name}", cancellationToken);
    }

    private static string BackupUrl(RequestContext context, string name) =>
        $"{context.BaseUrl}/storage/v1/object/{BackupBucket}/{context.UserId}/{Uri.EscapeDataString(name)}";

    private sealed record StorageEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);
}
