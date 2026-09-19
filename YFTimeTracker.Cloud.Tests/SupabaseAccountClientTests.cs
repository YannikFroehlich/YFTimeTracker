using System.Net;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Cloud.Tests;

[TestClass]
public sealed class SupabaseAccountClientTests
{
    private const string DeviceId = "11111111-1111-1111-1111-111111111111";

    private static (SupabaseAccountClient Client, RecordingHandler Handler) CreateClient()
    {
        var handler = new RecordingHandler
        {
            Fallback = request =>
            {
                if (request.Method == HttpMethod.Post && request.Uri.AbsolutePath.EndsWith("/devices"))
                {
                    return RecordingHandler.Create(
                        HttpStatusCode.Created,
                        $$"""[{"id":"{{DeviceId}}","user_id":"user-1","machine_key":"machine-key-1","device_name":"TEST-PC","last_seen_at":"2026-09-15T12:00:00Z"}]""");
                }

                return request.Method == HttpMethod.Get
                    ? RecordingHandler.Create(HttpStatusCode.OK, "[]")
                    : RecordingHandler.Create(HttpStatusCode.OK, string.Empty);
            }
        };

        var client = new SupabaseAccountClient(
            new HttpClient(handler),
            new StaticConnectionProvider(new CloudConnectionSettings("https://demo.supabase.co", "anon-key")),
            new StubAuthService("user-1", "access-1"),
            new StubDeviceIdentity(),
            new MutableClock(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)));

        return (client, handler);
    }

    private static AccountPush PushWith(
        AccountSnapshot? upserts = null,
        IReadOnlyDictionary<SyncEntityKind, IReadOnlyList<string>>? deletions = null) =>
        new(upserts ?? AccountSnapshot.Empty, deletions ?? new Dictionary<SyncEntityKind, IReadOnlyList<string>>());

    private static RecordedRequest RequestTo(RecordingHandler handler, HttpMethod method, string table) =>
        handler.Requests.Single(request =>
            request.Method == method && request.Uri.AbsolutePath.EndsWith($"/{table}"));

    [TestMethod]
    public async Task Profile_is_upserted_on_the_user_alone()
    {
        var (client, handler) = CreateClient();
        var push = PushWith(AccountSnapshot.Empty with
        {
            Profile = new CloudProfile("Yannik", "#3182FF", DateTimeOffset.UnixEpoch)
        });

        await client.PushAsync(push, progress: null, CancellationToken.None);

        // "profiles" hat genau eine Zeile je Konto und keine identity-Spalte. Ein
        // Konfliktziel mit identity quittiert PostgREST mit
        // 'column "identity" does not exist'.
        var request = RequestTo(handler, HttpMethod.Post, "profiles");
        StringAssert.Contains(request.Uri.Query, "on_conflict=user_id");
        Assert.IsFalse(
            request.Uri.Query.Contains("identity", StringComparison.Ordinal),
            "Das Profil darf nicht über identity aufgelöst werden.");
        StringAssert.Contains(request.Body, "Yannik");
    }

    [TestMethod]
    public async Task Identity_based_tables_are_upserted_on_user_and_identity()
    {
        var (client, handler) = CreateClient();
        var push = PushWith(AccountSnapshot.Empty with
        {
            Games =
            [
                new CloudGame("game:steam:440", null, "hash", "Team Fortress 2", GameSource.Steam,
                    "440", DateTimeOffset.UnixEpoch, null, null, false)
            ]
        });

        await client.PushAsync(push, progress: null, CancellationToken.None);

        StringAssert.Contains(RequestTo(handler, HttpMethod.Post, "games").Uri.Query, "on_conflict=user_id,identity");
    }

    [TestMethod]
    public async Task Deleting_marks_rows_instead_of_removing_them()
    {
        var (client, handler) = CreateClient();
        var push = PushWith(deletions: new Dictionary<SyncEntityKind, IReadOnlyList<string>>
        {
            [SyncEntityKind.Game] = ["game:manual:loeschtest"]
        });

        await client.PushAsync(push, progress: null, CancellationToken.None);

        // Verschwände die Zeile wirklich, könnte ein zweiter PC nicht unterscheiden,
        // ob sie gelöscht wurde oder dort nie ankam - und lüde sie wieder hoch.
        Assert.IsFalse(
            handler.Requests.Any(request => request.Method == HttpMethod.Delete),
            "Gelöscht wird im Konto nur markiert, nie wirklich entfernt.");

        var patch = handler.Requests.Single(request => request.Method == HttpMethod.Patch);
        StringAssert.Contains(patch.Uri.AbsolutePath, "/games");
        StringAssert.Contains(patch.Body, "deleted_at");
        StringAssert.Contains(Uri.UnescapeDataString(patch.Uri.Query), "game:manual:loeschtest");
    }

    [TestMethod]
    public async Task Uploading_a_row_again_lifts_an_earlier_deletion()
    {
        var (client, handler) = CreateClient();
        var push = PushWith(AccountSnapshot.Empty with
        {
            Games =
            [
                new CloudGame("game:manual:wieder da", null, "hash", "Wieder da", GameSource.Manual,
                    null, DateTimeOffset.UnixEpoch, null, null, false)
            ]
        });

        await client.PushAsync(push, progress: null, CancellationToken.None);

        // Ein Spiel, das neu angelegt wurde, nachdem es einmal gelöscht war, muss
        // den Grabstein im Konto wieder aufheben.
        StringAssert.Contains(RequestTo(handler, HttpMethod.Post, "games").Body, "\"deleted_at\":null");
    }

    [TestMethod]
    public async Task Rows_of_a_deleted_game_count_as_deleted_too()
    {
        const string DeletedGame = """
            [{"id":"g1","user_id":"user-1","identity":"game:manual:weg","content_hash":"h","name":"Weg",
              "source":0,"added_at_utc":"2026-01-01T00:00:00Z","is_pinned":false,
              "deleted_at":"2026-09-15T20:00:00Z","updated_at":"2026-09-15T20:00:00Z"}]
            """;

        const string LiveExecutable = """
            [{"id":"e1","user_id":"user-1","identity":"exe:game:manual:weg:x","content_hash":"h",
              "game_identity":"game:manual:weg","executable_path":"x.exe","executable_path_key":"X.EXE",
              "executable_name":"x.exe","is_primary":true,"added_at_utc":"2026-01-01T00:00:00Z",
              "deleted_at":null,"updated_at":"2026-09-15T20:00:00Z"}]
            """;

        var (client, handler) = CreateClient();
        handler.Fallback = request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                return RecordingHandler.Create(HttpStatusCode.OK, string.Empty);
            }

            if (request.Uri.AbsolutePath.EndsWith("/games"))
            {
                return RecordingHandler.Create(HttpStatusCode.OK, DeletedGame);
            }

            return request.Uri.AbsolutePath.EndsWith("/game_executables")
                ? RecordingHandler.Create(HttpStatusCode.OK, LiveExecutable)
                : RecordingHandler.Create(HttpStatusCode.OK, "[]");
        };

        var snapshot = await client.FetchAsync(progress: null, CancellationToken.None);

        // Die EXE-Zeile trägt selbst kein deleted_at. Ohne diese Ableitung würde sie
        // bei jedem Abgleich als "neu" gezählt und doch jedes Mal verworfen, weil ihr
        // Spiel lokal nicht mehr existiert.
        Assert.IsNotNull(snapshot.Executables.Single().DeletedAt);
    }

    [TestMethod]
    public async Task Fetch_reads_every_table_of_the_account()
    {
        var (client, handler) = CreateClient();

        await client.FetchAsync(progress: null, CancellationToken.None);

        var read = handler.Requests
            .Where(request => request.Method == HttpMethod.Get)
            .Select(request => request.Uri.AbsolutePath.Split('/').Last())
            .Distinct()
            .ToList();

        CollectionAssert.AreEquivalent(
            new[] { "games", "game_executables", "game_tags", "game_sessions", "tracking_exclusions", "app_settings", "game_artwork", "profiles" },
            read);
    }
}
