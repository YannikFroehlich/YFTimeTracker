using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YFTimeTracker.Cloud;

internal static class SupabaseJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Fehler von Supabase kommen je nach Endpunkt und Version in unterschiedlichen
/// Formen: mal <c>msg</c>, mal <c>message</c>, mal <c>error_description</c>.
/// Statt sich auf eine Form zu verlassen, werden alle bekannten Felder geprueft
/// und sonst der Rohtext gekuerzt zurueckgegeben.
/// </summary>
internal static class SupabaseErrorReader
{
    private static readonly string[] MessageFields =
        ["msg", "message", "error_description", "error", "hint", "details"];

    public static string Describe(HttpStatusCode statusCode, string? body)
    {
        var parsed = TryReadMessage(body);
        if (!string.IsNullOrWhiteSpace(parsed))
        {
            return parsed;
        }

        return string.IsNullOrWhiteSpace(body)
            ? $"Supabase antwortete mit Status {(int)statusCode}."
            : $"Supabase antwortete mit Status {(int)statusCode}: {Shorten(body)}";
    }

    public static string? TryReadMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var field in MessageFields)
            {
                if (document.RootElement.TryGetProperty(field, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    public static string? TryReadErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var field in new[] { "error_code", "error", "code" })
            {
                if (document.RootElement.TryGetProperty(field, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string Shorten(string value) =>
        value.Length <= 300 ? value : value[..300] + "...";
}

/// <summary>Fehler aus einem REST- oder Storage-Aufruf, bereits mit deutscher Meldung.</summary>
public sealed class CloudRequestException(string message, HttpStatusCode statusCode)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
