using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud;

/// <summary>
/// Liest Projekt-URL und Publishable Key aus <c>cloud.config.json</c> neben der
/// Anwendung.
///
/// Die Datei liegt bewusst nicht im Repository (siehe <c>.gitignore</c>): der
/// Publishable Key ist zwar fuer Clients gedacht und durch Row Level Security
/// abgesichert, laut AGENTS.md gehoeren Schluessel aber trotzdem nicht in den
/// Quellcode. Im Release wird die Datei beim Bauen beigelegt, sodass Benutzer
/// sich schlicht anmelden koennen, statt erst ein eigenes Supabase-Projekt
/// einzurichten.
///
/// Fehlt die Datei, bleibt der Kontoabgleich einfach aus - die App laeuft
/// unveraendert lokal weiter.
/// </summary>
public sealed class CloudConfigConnectionProvider(
    ILogger<CloudConfigConnectionProvider>? logger = null) : ICloudConnectionProvider
{
    internal const string ConfigFileName = "cloud.config.json";

    private readonly ILogger<CloudConfigConnectionProvider> log =
        logger ?? NullLogger<CloudConfigConnectionProvider>.Instance;

    private CloudConnectionSettings? cached;
    private bool loaded;

    public Task<CloudConnectionSettings?> GetAsync(CancellationToken cancellationToken)
    {
        if (loaded)
        {
            return Task.FromResult(cached);
        }

        loaded = true;
        cached = Load();
        return Task.FromResult(cached);
    }

    private CloudConnectionSettings? Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(path))
        {
            log.LogInformation(
                "{File} nicht gefunden; der Kontoabgleich bleibt in diesem Build deaktiviert", ConfigFileName);
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<CloudConfigFile>(
                File.ReadAllText(path),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (document is null ||
                string.IsNullOrWhiteSpace(document.ProjectUrl) ||
                string.IsNullOrWhiteSpace(document.PublishableKey))
            {
                log.LogWarning("{File} ist unvollständig; erwartet werden projectUrl und publishableKey", ConfigFileName);
                return null;
            }

            return new CloudConnectionSettings(document.ProjectUrl.Trim(), document.PublishableKey.Trim());
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            log.LogWarning(exception, "{File} konnte nicht gelesen werden", ConfigFileName);
            return null;
        }
    }

    private sealed record CloudConfigFile
    {
        [JsonPropertyName("projectUrl")] public string? ProjectUrl { get; init; }

        /// <summary>
        /// Heisst im Supabase-Dashboard "Publishable key"; in aelteren Projekten
        /// "anon key". Beide Formen funktionieren unveraendert.
        /// </summary>
        [JsonPropertyName("publishableKey")] public string? PublishableKey { get; init; }
    }
}
