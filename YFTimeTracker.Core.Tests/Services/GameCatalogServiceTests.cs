using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class GameCatalogServiceTests
{
    // Die ViewModels zeigen die Meldung einer YFTimeTrackerException unverändert in der
    // Statuszeile an. Rutscht hier eine ArgumentException durch, steht dort englischer
    // Framework-Text samt Parametername.
    [TestMethod]
    public async Task Adding_a_game_without_an_executable_reports_a_german_message()
    {
        var (catalog, _, _) = CreateCatalog();

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.AddGameAsync("   ", "Alpha", CancellationToken.None));

        Assert.AreEqual("Bitte wähle eine .exe-Datei aus.", exception.Message);
    }

    [TestMethod]
    public async Task Adding_a_game_with_a_non_executable_reports_a_german_message()
    {
        var (catalog, _, _) = CreateCatalog();

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.AddGameAsync(@"C:\Games\readme.txt", "Alpha", CancellationToken.None));

        Assert.AreEqual("Bitte wähle eine .exe-Datei aus.", exception.Message);
    }

    [TestMethod]
    public async Task Updating_a_game_without_a_display_name_reports_a_german_message()
    {
        var (catalog, _, _) = CreateCatalog();
        var game = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.UpdateGameAsync(
                game.Id,
                "  ",
                @"C:\Games\Alpha.exe",
                null,
                null,
                [],
                CancellationToken.None));

        Assert.AreEqual("Bitte gib einen Anzeigenamen an.", exception.Message);
    }

    [TestMethod]
    public async Task Updating_a_game_without_an_executable_reports_a_german_message()
    {
        var (catalog, _, _) = CreateCatalog();
        var game = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.UpdateGameAsync(game.Id, "Alpha", string.Empty, null, null, [], CancellationToken.None));

        Assert.AreEqual("Bitte wähle eine .exe-Datei aus.", exception.Message);
    }

    [TestMethod]
    public async Task Updating_tags_trims_and_deduplicates_case_insensitively()
    {
        var (catalog, _, _) = CreateCatalog();
        var game = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);

        await catalog.UpdateGameAsync(
            game.Id,
            "Alpha",
            @"C:\Games\Alpha.exe",
            null,
            null,
            ["  Shooter ", "Multiplayer", "shooter", ""],
            CancellationToken.None);

        var stored = (await catalog.GetGamesAsync(CancellationToken.None)).Single(candidate => candidate.Id == game.Id);
        CollectionAssert.AreEqual(new[] { "Shooter", "Multiplayer" }, stored.Tags.Select(tag => tag.Tag).ToArray());
    }

    [TestMethod]
    public async Task Merging_a_game_into_itself_is_rejected()
    {
        var (catalog, _, _) = CreateCatalog();
        var game = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.MergeGamesAsync(game.Id, game.Id, CancellationToken.None));

        Assert.AreEqual("Ein Spiel kann nicht mit sich selbst zusammengeführt werden.", exception.Message);
    }

    [TestMethod]
    public async Task Merging_reports_a_missing_target_instead_of_touching_the_source()
    {
        var (catalog, games, _) = CreateCatalog();
        var game = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);

        await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.MergeGamesAsync(game.Id, 4242, CancellationToken.None));

        Assert.IsNull(games.LastMergePlan);
        Assert.HasCount(1, await catalog.GetGamesAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task A_running_session_blocks_the_merge()
    {
        var (catalog, games, sessions) = CreateCatalog();
        var source = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);
        var target = await catalog.AddGameAsync(@"C:\Games\Beta.exe", "Beta", CancellationToken.None);
        await sessions.AddAsync(OpenSession(source.Id), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.MergeGamesAsync(source.Id, target.Id, CancellationToken.None));

        StringAssert.Contains(exception.Message, "Pausiere zuerst das Tracking");
        Assert.IsNull(games.LastMergePlan, "Bei laufender Session darf nichts geschrieben werden.");
    }

    [TestMethod]
    public async Task Merging_hands_down_the_plan_and_reports_what_moved()
    {
        var (catalog, games, sessions) = CreateCatalog();
        var source = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);
        var target = await catalog.AddGameAsync(@"C:\Games\Beta.exe", "Beta", CancellationToken.None);
        await sessions.AddAsync(ClosedSession(target.Id, "10:00", "11:30"), CancellationToken.None);
        await sessions.AddAsync(ClosedSession(source.Id, "11:00", "12:00"), CancellationToken.None);
        await sessions.AddAsync(ClosedSession(source.Id, "20:00", "21:00"), CancellationToken.None);

        var result = await catalog.MergeGamesAsync(source.Id, target.Id, CancellationToken.None);

        Assert.AreEqual((source.Id, target.Id), games.LastMergeGameIds);
        Assert.AreEqual(target.Id, result.TargetGameId);
        Assert.AreEqual("Beta", result.TargetGameName);
        Assert.AreEqual(2, result.MovedSessionCount);
        Assert.AreEqual(1, result.CombinedSessionCount, "Die 11:00-Session geht in der 10:00-Session auf.");
        Assert.AreEqual(1, result.MovedExecutableCount);
    }

    private static GameSession ClosedSession(long gameId, string start, string end) => new()
    {
        GameId = gameId,
        StartedAtUtc = At(start),
        LastSeenAtUtc = At(end),
        EndedAtUtc = At(end),
        DurationSeconds = (long)(At(end) - At(start)).TotalSeconds,
        BootSessionId = "boot"
    };

    private static GameSession OpenSession(long gameId) => new()
    {
        GameId = gameId,
        StartedAtUtc = At("10:00"),
        LastSeenAtUtc = At("10:30"),
        BootSessionId = "boot"
    };

    private static DateTimeOffset At(string time) => DateTimeOffset.Parse($"2026-09-08T{time}:00Z");

    private static (GameCatalogService Catalog, InMemoryGameRepository Games, InMemoryGameSessionRepository Sessions) CreateCatalog()
    {
        var games = new InMemoryGameRepository();
        var sessions = new InMemoryGameSessionRepository(gameId =>
            games.GetByIdAsync(gameId, CancellationToken.None).GetAwaiter().GetResult());
        var catalog = new GameCatalogService(
            games,
            sessions,
            new FakeClock(DateTimeOffset.Parse("2026-09-08T23:00:00Z")));
        return (catalog, games, sessions);
    }
}
