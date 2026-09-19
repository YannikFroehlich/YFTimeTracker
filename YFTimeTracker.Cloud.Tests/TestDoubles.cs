using System.Net;
using System.Text;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud.Tests;

/// <summary>Zeichnet jede Anfrage auf und beantwortet sie aus einer Warteschlange.</summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<RecordedRequest, HttpResponseMessage>> responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public Func<RecordedRequest, HttpResponseMessage>? Fallback { get; set; }

    public RecordingHandler RespondWith(HttpStatusCode statusCode, string body = "")
    {
        responses.Enqueue(_ => Create(statusCode, body));
        return this;
    }

    public RecordingHandler RespondWith(Func<RecordedRequest, HttpResponseMessage> factory)
    {
        responses.Enqueue(factory);
        return this;
    }

    public static HttpResponseMessage Create(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers
                .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase));

        Requests.Add(recorded);

        if (responses.Count > 0)
        {
            return responses.Dequeue()(recorded);
        }

        return Fallback?.Invoke(recorded) ?? Create(HttpStatusCode.OK, "[]");
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string Body,
    IReadOnlyDictionary<string, string> Headers);

internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public string? Read(string name) => values.GetValueOrDefault(name);

    public void Write(string name, string secret) => values[name] = secret;

    public void Delete(string name) => values.Remove(name);

    public bool Contains(string name) => values.ContainsKey(name);
}

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(values.GetValueOrDefault(key));

    public Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        values[key] = value;
        return Task.CompletedTask;
    }

    public Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken) =>
        Task.FromResult(values.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed) ? parsed : fallback);

    public Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken) =>
        Task.FromResult(values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var parsed) ? parsed : fallback);
}

internal sealed class StaticConnectionProvider(CloudConnectionSettings? settings) : ICloudConnectionProvider
{
    private CloudConnectionSettings? current = settings;

    public Task<CloudConnectionSettings?> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(current);

    public Task SaveAsync(CloudConnectionSettings value, CancellationToken cancellationToken)
    {
        current = value;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        current = null;
        return Task.CompletedTask;
    }
}

internal sealed class MutableClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed class StubDeviceIdentity : IDeviceIdentityProvider
{
    public string MachineKey => "machine-key-1";

    public string DeviceName => "TEST-PC";
}

internal sealed class StubAuthService(string userId, string? accessToken) : ICloudAuthService
{
    public CloudSession? CurrentSession { get; } = accessToken is null
        ? null
        : new CloudSession(userId, "test@example.com", accessToken, "refresh", DateTimeOffset.MaxValue);

    public bool IsSignedIn => CurrentSession is not null;

    public event EventHandler? SessionChanged
    {
        add { }
        remove { }
    }

    public Task<CloudAuthResult> SignInAsync(string email, string password, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CloudAuthResult> SignUpAsync(string email, string password, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CloudAuthResult> RestoreSessionAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        Task.FromResult(CurrentSession?.AccessToken);

    public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
