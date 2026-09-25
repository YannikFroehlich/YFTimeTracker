using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud;

/// <summary>
/// Anmeldung gegen Supabase Auth (GoTrue).
///
/// Wichtig fuer die Datensicherheit: das Passwort wird ausschliesslich fuer den
/// einen Anmeldeaufruf verwendet und nirgends abgelegt. Dauerhaft gespeichert
/// wird nur der Refresh-Token, und der landet im Windows-Anmelde-
/// informationsspeicher, nicht in der SQLite-Datenbank und nicht im Klartext.
/// </summary>
public sealed class SupabaseAuthService(
    HttpClient httpClient,
    ICloudConnectionProvider connectionProvider,
    ISecretStore secretStore,
    ISettingsStore settingsStore,
    IClock clock,
    ILogger<SupabaseAuthService>? logger = null) : ICloudAuthService
{
    internal const string RefreshTokenSecretName = "YFTimeTracker.Supabase.RefreshToken";

    private readonly ILogger<SupabaseAuthService> log = logger ?? NullLogger<SupabaseAuthService>.Instance;
    private readonly SemaphoreSlim gate = new(1, 1);

    public CloudSession? CurrentSession { get; private set; }

    public bool IsSignedIn => CurrentSession is not null;

    public event EventHandler? SessionChanged;

    public Task<CloudAuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken) =>
        AuthenticateAsync(
            "token?grant_type=password",
            new PasswordGrantRequest(email.Trim(), password),
            cancellationToken);

    public async Task<CloudAuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetAsync(cancellationToken);
        if (connection is not { IsComplete: true })
        {
            return NotConfigured();
        }

        try
        {
            using var response = await SendAsync(
                connection, "signup", new PasswordGrantRequest(email.Trim(), password), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return TranslateFailure(response.StatusCode, body);
            }

            var payload = JsonSerializer.Deserialize<TokenResponse>(body, SupabaseJson.Options);

            // Ist die E-Mail-Bestaetigung aktiv, liefert Supabase nur das Benutzer-
            // objekt ohne Token zurueck. Das ist kein Fehler, sondern der Normalfall.
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                return new CloudAuthResult(
                    CloudAuthStatus.EmailConfirmationRequired,
                    "Konto angelegt. Bitte bestätige zuerst die E-Mail von Supabase und melde dich dann an.");
            }

            return await PersistSessionAsync(payload, email.Trim(), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return NetworkError(exception);
        }
    }

    public async Task<CloudAuthResult> RestoreSessionAsync(CancellationToken cancellationToken)
    {
        // Dieselbe Sperre wie GetAccessTokenAsync: Supabase tauscht den Refresh-Token
        // bei jeder Erneuerung aus. Zwei parallele Erneuerungen mit demselben Token
        // wertet Supabase als Wiederverwendung und widerruft die ganze Sitzung.
        await gate.WaitAsync(cancellationToken);
        try
        {
            var refreshToken = secretStore.Read(RefreshTokenSecretName);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new CloudAuthResult(CloudAuthStatus.InvalidCredentials, "Nicht angemeldet.");
            }

            return await RefreshAsync(refreshToken, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (CurrentSession is { } session && !session.IsExpired(clock.UtcNow))
            {
                return session.AccessToken;
            }

            var refreshToken = CurrentSession?.RefreshToken ?? secretStore.Read(RefreshTokenSecretName);
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return null;
            }

            var result = await RefreshAsync(refreshToken, cancellationToken);
            return result.IsSuccess ? result.Session?.AccessToken : null;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CloudAuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetAsync(cancellationToken);
        if (connection is not { IsComplete: true })
        {
            return NotConfigured();
        }

        try
        {
            using var response = await SendAsync(
                connection, "token?grant_type=refresh_token", new RefreshGrantRequest(refreshToken), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Ein abgelehnter Refresh-Token ist endgueltig: er wurde widerrufen
                // oder ist abgelaufen. Ihn zu behalten wuerde bei jedem Start einen
                // aussichtslosen Versuch ausloesen.
                if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
                {
                    ClearSession();
                }

                return TranslateFailure(response.StatusCode, body);
            }

            var payload = JsonSerializer.Deserialize<TokenResponse>(body, SupabaseJson.Options);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                return new CloudAuthResult(
                    CloudAuthStatus.UnexpectedError,
                    "Unerwartete Antwort beim Erneuern der Sitzung.");
            }

            var email = payload.User?.Email
                ?? await settingsStore.GetAsync(AppSettingKeys.CloudUserEmail, cancellationToken)
                ?? string.Empty;
            return await PersistSessionAsync(payload, email, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return NetworkError(exception);
        }
    }

    private async Task<CloudAuthResult> AuthenticateAsync(
        string path,
        object request,
        CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetAsync(cancellationToken);
        if (connection is not { IsComplete: true })
        {
            return NotConfigured();
        }

        try
        {
            using var response = await SendAsync(connection, path, request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return TranslateFailure(response.StatusCode, body);
            }

            var payload = JsonSerializer.Deserialize<TokenResponse>(body, SupabaseJson.Options);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                return new CloudAuthResult(CloudAuthStatus.UnexpectedError, "Unerwartete Antwort von Supabase.");
            }

            return await PersistSessionAsync(payload, payload.User?.Email ?? string.Empty, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return NetworkError(exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        CloudConnectionSettings connection,
        string path,
        object request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{connection.NormalizedUrl}/auth/v1/{path}")
        {
            Content = JsonContent.Create(request, options: SupabaseJson.Options)
        };
        message.Headers.TryAddWithoutValidation("apikey", connection.AnonKey);
        return await httpClient.SendAsync(message, cancellationToken);
    }

    private async Task<CloudAuthResult> PersistSessionAsync(
        TokenResponse payload,
        string fallbackEmail,
        CancellationToken cancellationToken)
    {
        var email = payload.User?.Email ?? fallbackEmail;
        var session = new CloudSession(
            payload.User?.Id ?? string.Empty,
            email,
            payload.AccessToken!,
            payload.RefreshToken ?? string.Empty,
            clock.UtcNow.AddSeconds(payload.ExpiresIn <= 0 ? 3600 : payload.ExpiresIn));

        CurrentSession = session;

        if (!string.IsNullOrEmpty(session.RefreshToken))
        {
            secretStore.Write(RefreshTokenSecretName, session.RefreshToken);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            await settingsStore.SetAsync(AppSettingKeys.CloudUserEmail, email, cancellationToken);
        }

        SessionChanged?.Invoke(this, EventArgs.Empty);
        return CloudAuthResult.Success(session);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetAsync(cancellationToken);
        var accessToken = CurrentSession?.AccessToken;

        // Erst lokal abmelden: der Benutzer hat abgemeldet geklickt, und das muss
        // auch dann gelten, wenn Supabase gerade nicht erreichbar ist.
        ClearSession();

        if (connection is not { IsComplete: true } || string.IsNullOrEmpty(accessToken))
        {
            return;
        }

        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post, $"{connection.NormalizedUrl}/auth/v1/logout");
            message.Headers.TryAddWithoutValidation("apikey", connection.AnonKey);
            message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            using var response = await httpClient.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            log.LogInformation(exception, "Abmelden bei Supabase nicht bestätigt; lokale Sitzung wurde trotzdem verworfen");
        }
    }

    private void ClearSession()
    {
        CurrentSession = null;
        secretStore.Delete(RefreshTokenSecretName);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static CloudAuthResult NotConfigured() => new(
        CloudAuthStatus.NotConfigured,
        "Projekt-URL und Publishable Key sind noch nicht hinterlegt.");

    private CloudAuthResult NetworkError(Exception exception)
    {
        log.LogWarning(exception, "Supabase ist nicht erreichbar");
        return new CloudAuthResult(
            CloudAuthStatus.NetworkError,
            "Supabase ist nicht erreichbar. Bitte Internetverbindung und Projekt-URL prüfen.");
    }

    private static CloudAuthResult TranslateFailure(HttpStatusCode statusCode, string? body)
    {
        var errorCode = SupabaseErrorReader.TryReadErrorCode(body);
        var message = SupabaseErrorReader.TryReadMessage(body) ?? string.Empty;

        if (errorCode is "email_not_confirmed" ||
            message.Contains("not confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return new CloudAuthResult(
                CloudAuthStatus.EmailConfirmationRequired,
                "Die E-Mail-Adresse ist noch nicht bestätigt. Bitte zuerst den Link aus der Bestätigungsmail öffnen.");
        }

        if (statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            return new CloudAuthResult(
                CloudAuthStatus.InvalidCredentials,
                string.IsNullOrWhiteSpace(message)
                    ? "Anmeldung fehlgeschlagen. Bitte E-Mail und Passwort prüfen."
                    : $"Anmeldung fehlgeschlagen: {message}");
        }

        return new CloudAuthResult(
            CloudAuthStatus.UnexpectedError,
            SupabaseErrorReader.Describe(statusCode, body));
    }

    private sealed record PasswordGrantRequest(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string Password);

    private sealed record RefreshGrantRequest(
        [property: JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }

        [JsonPropertyName("user")] public UserResponse? User { get; init; }
    }

    private sealed record UserResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }

        [JsonPropertyName("email")] public string? Email { get; init; }
    }
}
