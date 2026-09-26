using System.Net;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud.Tests;

[TestClass]
public sealed class SupabaseAuthServiceTests
{
    private const string TokenResponseBody = """
        {
          "access_token": "access-1",
          "refresh_token": "refresh-1",
          "expires_in": 3600,
          "user": { "id": "user-1", "email": "spieler@example.de" }
        }
        """;

    private static (SupabaseAuthService Service, RecordingHandler Handler, InMemorySecretStore Secrets, InMemorySettingsStore Settings, MutableClock Clock)
        CreateService(CloudConnectionSettings? connection = null)
    {
        var handler = new RecordingHandler();
        var secrets = new InMemorySecretStore();
        var settings = new InMemorySettingsStore();
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var service = new SupabaseAuthService(
            new HttpClient(handler),
            new StaticConnectionProvider(connection ?? new CloudConnectionSettings("https://demo.supabase.co", "anon-key")),
            secrets,
            settings,
            clock);

        return (service, handler, secrets, settings, clock);
    }

    [TestMethod]
    public async Task Sign_in_stores_only_the_refresh_token()
    {
        var (service, handler, secrets, settings, _) = CreateService();
        handler.RespondWith(HttpStatusCode.OK, TokenResponseBody);

        var result = await service.SignInAsync("spieler@example.de", "geheim", CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("refresh-1", secrets.Read(SupabaseAuthService.RefreshTokenSecretName));
        Assert.AreEqual("spieler@example.de", await settings.GetAsync(AppSettingKeys.CloudUserEmail, CancellationToken.None));

        // Das Passwort geht genau einmal an Supabase und wird nirgends abgelegt.
        Assert.AreEqual(1, handler.Requests.Count);
        StringAssert.Contains(handler.Requests[0].Body, "geheim");
        Assert.IsFalse(secrets.Contains("password"));
    }

    [TestMethod]
    public async Task Sign_in_sends_the_anon_key_as_apikey_header()
    {
        var (service, handler, _, _, _) = CreateService();
        handler.RespondWith(HttpStatusCode.OK, TokenResponseBody);

        await service.SignInAsync("spieler@example.de", "geheim", CancellationToken.None);

        Assert.AreEqual("anon-key", handler.Requests[0].Headers["apikey"]);
        StringAssert.Contains(handler.Requests[0].Uri.ToString(), "grant_type=password");
    }

    [TestMethod]
    public async Task Wrong_password_is_reported_as_invalid_credentials()
    {
        var (service, handler, secrets, _, _) = CreateService();
        handler.RespondWith(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Invalid login credentials"}""");

        var result = await service.SignInAsync("spieler@example.de", "falsch", CancellationToken.None);

        Assert.AreEqual(CloudAuthStatus.InvalidCredentials, result.Status);
        Assert.IsFalse(secrets.Contains(SupabaseAuthService.RefreshTokenSecretName));
    }

    [TestMethod]
    public async Task Unconfirmed_email_is_reported_separately_from_a_wrong_password()
    {
        var (service, handler, _, _, _) = CreateService();
        handler.RespondWith(
            HttpStatusCode.BadRequest,
            """{"error_code":"email_not_confirmed","msg":"Email not confirmed"}""");

        var result = await service.SignInAsync("spieler@example.de", "geheim", CancellationToken.None);

        Assert.AreEqual(CloudAuthStatus.EmailConfirmationRequired, result.Status);
    }

    [TestMethod]
    public async Task Too_short_password_on_sign_up_gets_a_german_message()
    {
        var (service, handler, _, _, _) = CreateService();
        handler.RespondWith(
            HttpStatusCode.UnprocessableEntity,
            """{"error_code":"weak_password","msg":"Password should be at least 8 characters."}""");

        var result = await service.SignUpAsync("spieler@example.de", "kurz", CancellationToken.None);

        Assert.AreEqual(CloudAuthStatus.InvalidCredentials, result.Status);
        StringAssert.Contains(result.Message, "mindestens 8 Zeichen");
    }

    [TestMethod]
    public async Task Sign_up_without_a_token_asks_the_user_to_confirm_the_email()
    {
        var (service, handler, secrets, _, _) = CreateService();

        // Bei aktiver E-Mail-Bestaetigung liefert Supabase nur das Benutzerobjekt.
        handler.RespondWith(
            HttpStatusCode.OK,
            """{"id":"user-1","email":"spieler@example.de","confirmation_sent_at":"2026-09-15T12:00:00Z"}""");

        var result = await service.SignUpAsync("spieler@example.de", "geheim", CancellationToken.None);

        Assert.AreEqual(CloudAuthStatus.EmailConfirmationRequired, result.Status);
        Assert.IsFalse(service.IsSignedIn);
        Assert.IsFalse(secrets.Contains(SupabaseAuthService.RefreshTokenSecretName));
    }

    [TestMethod]
    public async Task Sign_up_that_returns_a_token_signs_the_user_in_directly()
    {
        var (service, handler, secrets, _, _) = CreateService();
        handler.RespondWith(HttpStatusCode.OK, TokenResponseBody);

        var result = await service.SignUpAsync("spieler@example.de", "geheim", CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(service.IsSignedIn);
        Assert.AreEqual("refresh-1", secrets.Read(SupabaseAuthService.RefreshTokenSecretName));
    }

    [TestMethod]
    public async Task Expired_access_token_is_refreshed_before_it_is_handed_out()
    {
        var (service, handler, _, _, clock) = CreateService();
        handler.RespondWith(HttpStatusCode.OK, TokenResponseBody);
        await service.SignInAsync("spieler@example.de", "geheim", CancellationToken.None);

        handler.RespondWith(
            HttpStatusCode.OK,
            """
            {
              "access_token": "access-2",
              "refresh_token": "refresh-2",
              "expires_in": 3600,
              "user": { "id": "user-1", "email": "spieler@example.de" }
            }
            """);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        var token = await service.GetAccessTokenAsync(CancellationToken.None);

        Assert.AreEqual("access-2", token);
        StringAssert.Contains(handler.Requests[^1].Uri.ToString(), "grant_type=refresh_token");
    }

    [TestMethod]
    public async Task Valid_access_token_is_reused_without_a_network_call()
    {
        var (service, handler, _, _, _) = CreateService();
        handler.RespondWith(HttpStatusCode.OK, TokenResponseBody);
        await service.SignInAsync("spieler@example.de", "geheim", CancellationToken.None);

        var token = await service.GetAccessTokenAsync(CancellationToken.None);

        Assert.AreEqual("access-1", token);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Rejected_refresh_token_is_discarded_instead_of_retried_forever()
    {
        var (service, handler, secrets, _, _) = CreateService();
        secrets.Write(SupabaseAuthService.RefreshTokenSecretName, "widerrufen");
        handler.RespondWith(HttpStatusCode.BadRequest, """{"msg":"Invalid Refresh Token"}""");

        var result = await service.RestoreSessionAsync(CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(
            secrets.Contains(SupabaseAuthService.RefreshTokenSecretName),
            "Ein abgelehnter Refresh-Token muss verworfen werden, sonst scheitert jeder Start erneut daran.");
    }

    [TestMethod]
    public async Task Restore_and_token_request_at_the_same_time_refresh_only_once()
    {
        // Supabase tauscht den Refresh-Token bei jeder Erneuerung aus. Zwei parallele
        // Erneuerungen mit demselben Token wuerden als Wiederverwendung gewertet und
        // die Sitzung widerrufen.
        var handler = new BlockingTokenHandler(TokenResponseBody);
        var secrets = new InMemorySecretStore();
        secrets.Write(SupabaseAuthService.RefreshTokenSecretName, "refresh-0");
        var service = new SupabaseAuthService(
            new HttpClient(handler),
            new StaticConnectionProvider(new CloudConnectionSettings("https://demo.supabase.co", "anon-key")),
            secrets,
            new InMemorySettingsStore(),
            new MutableClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)));

        var restore = service.RestoreSessionAsync(CancellationToken.None);
        var token = service.GetAccessTokenAsync(CancellationToken.None);
        handler.Release();

        Assert.IsTrue((await restore).IsSuccess);
        Assert.AreEqual("access-1", await token);
        Assert.AreEqual(1, handler.RequestCount);
    }

    private sealed class BlockingTokenHandler(string body) : HttpMessageHandler
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requestCount;

        public int RequestCount => Volatile.Read(ref requestCount);

        public void Release() => release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requestCount);
            await release.Task;
            return RecordingHandler.Create(HttpStatusCode.OK, body);
        }
    }

    [TestMethod]
    public async Task Restore_without_a_stored_token_does_not_call_the_network()
    {
        var (service, handler, _, _, _) = CreateService();

        var result = await service.RestoreSessionAsync(CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Missing_connection_settings_are_reported_instead_of_attempted()
    {
        var (service, handler, _, _, _) = CreateService(new CloudConnectionSettings(string.Empty, string.Empty));

        var result = await service.SignInAsync("spieler@example.de", "geheim", CancellationToken.None);

        Assert.AreEqual(CloudAuthStatus.NotConfigured, result.Status);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task Sign_out_clears_the_session_even_if_supabase_is_unreachable()
    {
        var (service, handler, secrets, _, _) = CreateService();
        handler.RespondWith(HttpStatusCode.OK, TokenResponseBody);
        await service.SignInAsync("spieler@example.de", "geheim", CancellationToken.None);

        handler.RespondWith(_ => throw new HttpRequestException("kein Netz"));
        await service.SignOutAsync(CancellationToken.None);

        Assert.IsFalse(service.IsSignedIn);
        Assert.IsFalse(secrets.Contains(SupabaseAuthService.RefreshTokenSecretName));
    }
}
