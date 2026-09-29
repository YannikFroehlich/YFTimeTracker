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

    /// <summary>
    /// Anmeldung, bei der das Passwort stimmte, der Code aber noch fehlt. Nur im
    /// Arbeitsspeicher: der aal1-Token taugt wegen der Zwei-Faktor-Policies im
    /// Schema nicht fuer den Abgleich und wird nie gespeichert.
    /// </summary>
    private PendingSecondFactor? pendingSecondFactor;

    public CloudSession? CurrentSession { get; private set; }

    public bool IsSignedIn => CurrentSession is not null;

    public event EventHandler? SessionChanged;

    public Task<CloudAuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        pendingSecondFactor = null;
        return AuthenticateAsync(
            "token?grant_type=password",
            new PasswordGrantRequest(email.Trim(), password),
            cancellationToken);
    }

    public async Task<CloudAuthResult> VerifySecondFactorAsync(string code, CancellationToken cancellationToken)
    {
        if (pendingSecondFactor is not { } pending)
        {
            return new CloudAuthResult(
                CloudAuthStatus.InvalidCredentials,
                "Bitte zuerst mit E-Mail und Passwort anmelden.");
        }

        var connection = await connectionProvider.GetAsync(cancellationToken);
        if (connection is not { IsComplete: true })
        {
            return NotConfigured();
        }

        try
        {
            using var challengeResponse = await SendAsync(
                connection, $"factors/{pending.FactorId}/challenge", new { }, cancellationToken, pending.AccessToken);
            var challengeBody = await challengeResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!challengeResponse.IsSuccessStatusCode)
            {
                return SecondFactorFailure(challengeResponse.StatusCode, challengeBody);
            }

            var challenge = JsonSerializer.Deserialize<ChallengeResponse>(challengeBody, SupabaseJson.Options);
            if (string.IsNullOrEmpty(challenge?.Id))
            {
                return new CloudAuthResult(CloudAuthStatus.UnexpectedError, "Unerwartete Antwort von Supabase.");
            }

            using var verifyResponse = await SendAsync(
                connection,
                $"factors/{pending.FactorId}/verify",
                new VerifyRequest(challenge.Id, code.Trim().Replace(" ", string.Empty)),
                cancellationToken,
                pending.AccessToken);
            var verifyBody = await verifyResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!verifyResponse.IsSuccessStatusCode)
            {
                return SecondFactorFailure(verifyResponse.StatusCode, verifyBody);
            }

            var payload = JsonSerializer.Deserialize<TokenResponse>(verifyBody, SupabaseJson.Options);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
            {
                return new CloudAuthResult(CloudAuthStatus.UnexpectedError, "Unerwartete Antwort von Supabase.");
            }

            pendingSecondFactor = null;
            return await PersistSessionAsync(payload, payload.User?.Email ?? pending.Email, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return NetworkError(exception);
        }
    }

    private CloudAuthResult SecondFactorFailure(HttpStatusCode statusCode, string body)
    {
        // Der aal1-Token lebt nur kurz; ist er abgelaufen, hilft nur eine neue Anmeldung.
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            pendingSecondFactor = null;
            return new CloudAuthResult(
                CloudAuthStatus.InvalidCredentials,
                "Die Anmeldung ist abgelaufen. Bitte erneut mit E-Mail und Passwort anmelden.");
        }

        if (statusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            return new CloudAuthResult(
                CloudAuthStatus.SecondFactorRequired,
                "Der Code stimmt nicht oder ist abgelaufen. Bitte den aktuellen Code aus der Authenticator-App eingeben.");
        }

        return new CloudAuthResult(CloudAuthStatus.UnexpectedError, SupabaseErrorReader.Describe(statusCode, body));
    }

    /// <summary>
    /// Hat das Konto einen bestaetigten zweiten Faktor, der Token aber nur die
    /// Stufe aal1 (nur Passwort), liefert es die Id des Faktors.
    /// </summary>
    private static string? MissingSecondFactor(TokenResponse payload)
    {
        var factor = payload.User?.Factors?.FirstOrDefault(
            factor => factor.Status == "verified" && factor.FactorType == "totp" && !string.IsNullOrEmpty(factor.Id));
        return factor is not null && ReadAssuranceLevel(payload.AccessToken!) != "aal2" ? factor.Id : null;
    }

    /// <summary>Liest die Claim "aal" aus dem JWT. Die Signatur prueft Supabase, nicht die App.</summary>
    internal static string? ReadAssuranceLevel(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var segment = parts[1].Replace('-', '+').Replace('_', '/');
            segment = segment.PadRight(segment.Length + (4 - segment.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(segment));
            return document.RootElement.TryGetProperty("aal", out var aal) ? aal.GetString() : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

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

            // Zwei-Faktor-Anmeldung nachtraeglich auf der Website eingeschaltet: die
            // gespeicherte Sitzung hat nur aal1 und kommt nicht mehr an die Daten.
            if (MissingSecondFactor(payload) is not null)
            {
                ClearSession();
                return new CloudAuthResult(
                    CloudAuthStatus.SecondFactorRequired,
                    "Für dein Konto ist die Zwei-Faktor-Anmeldung aktiv. Bitte melde dich neu an und gib den Code aus deiner Authenticator-App ein.");
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

            if (MissingSecondFactor(payload) is { } factorId)
            {
                pendingSecondFactor = new PendingSecondFactor(payload.AccessToken, factorId, payload.User?.Email ?? string.Empty);
                return new CloudAuthResult(
                    CloudAuthStatus.SecondFactorRequired,
                    "Bitte den 6-stelligen Code aus deiner Authenticator-App eingeben.");
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
        CancellationToken cancellationToken,
        string? accessToken = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{connection.NormalizedUrl}/auth/v1/{path}")
        {
            Content = JsonContent.Create(request, options: SupabaseJson.Options)
        };
        message.Headers.TryAddWithoutValidation("apikey", connection.AnonKey);
        if (accessToken is not null)
        {
            message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
        }

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
        pendingSecondFactor = null;
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

        // Supabase antwortet mit 422 und englischem Text; die Mindestlaenge steht
        // im Projekt unter Authentication -> Email.
        if (errorCode is "weak_password")
        {
            return new CloudAuthResult(
                CloudAuthStatus.InvalidCredentials,
                "Das Passwort ist zu schwach. Bitte mindestens 8 Zeichen verwenden.");
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

        [JsonPropertyName("factors")] public IReadOnlyList<FactorResponse>? Factors { get; init; }
    }

    private sealed record FactorResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }

        [JsonPropertyName("status")] public string? Status { get; init; }

        [JsonPropertyName("factor_type")] public string? FactorType { get; init; }
    }

    private sealed record ChallengeResponse
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
    }

    private sealed record VerifyRequest(
        [property: JsonPropertyName("challenge_id")] string ChallengeId,
        [property: JsonPropertyName("code")] string Code);

    private sealed record PendingSecondFactor(string AccessToken, string FactorId, string Email);
}
