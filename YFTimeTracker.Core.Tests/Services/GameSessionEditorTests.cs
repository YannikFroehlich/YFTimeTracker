using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;
using YFTimeTracker.Core.Validation;

namespace YFTimeTracker.Core.Tests.Services;

[TestClass]
public sealed class GameSessionEditorTests
{
    [TestMethod]
    public async Task AddManualSessionAsync_rejects_overlap_for_same_game()
    {
        var games = new InMemoryGameRepository();
        var game = await games.AddAsync(new Game
        {
            Name = "Test",
            ExecutablePath = @"C:\Games\Test.exe",
            ExecutablePathKey = @"C:\GAMES\TEST.EXE",
            ExecutableName = "Test.exe",
            AddedAtUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var editor = new GameSessionEditor(
            games,
            sessions,
            new FakeBootSessionProvider("boot"),
            new FakeClock(DateTimeOffset.Parse("2026-08-31T00:00:00Z")));

        await editor.AddManualSessionAsync(game.Id, DateTimeOffset.Parse("2026-08-30T10:00:00Z"), DateTimeOffset.Parse("2026-08-30T11:00:00Z"), CancellationToken.None);

        try
        {
            await editor.AddManualSessionAsync(game.Id, DateTimeOffset.Parse("2026-08-30T10:30:00Z"), DateTimeOffset.Parse("2026-08-30T11:30:00Z"), CancellationToken.None);
            Assert.Fail("Expected overlapping sessions to be rejected.");
        }
        catch (YFTimeTrackerException)
        {
        }
    }

    [TestMethod]
    public async Task AddManualSessionAsync_rejects_overlap_with_running_session()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var games = new InMemoryGameRepository();
        var game = await AddGameAsync(games, now);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddHours(-2),
            LastSeenAtUtc = now,
            BootSessionId = "boot"
        }, CancellationToken.None);
        var editor = CreateEditor(games, sessions, now);

        await Assert.ThrowsAsync<YFTimeTrackerException>(() => editor.AddManualSessionAsync(
            game.Id,
            now.AddHours(-1),
            now.AddMinutes(-30),
            CancellationToken.None));
    }

    [TestMethod]
    public async Task UpdateManualSessionAsync_updates_completed_session_duration()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var games = new InMemoryGameRepository();
        var game = await AddGameAsync(games, now);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var session = await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddHours(-2),
            LastSeenAtUtc = now.AddHours(-1),
            EndedAtUtc = now.AddHours(-1),
            DurationSeconds = 3600,
            BootSessionId = "boot"
        }, CancellationToken.None);
        var editor = CreateEditor(games, sessions, now);

        await editor.UpdateManualSessionAsync(
            session.Id,
            now.AddMinutes(-90),
            now.AddMinutes(-15),
            CancellationToken.None);

        var stored = await sessions.GetByIdAsync(session.Id, CancellationToken.None);
        Assert.IsNotNull(stored);
        Assert.AreEqual(4500, stored.DurationSeconds);
    }

    [TestMethod]
    public async Task Running_session_cannot_be_edited_or_deleted()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var games = new InMemoryGameRepository();
        var game = await AddGameAsync(games, now);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var session = await sessions.AddAsync(new GameSession
        {
            GameId = game.Id,
            StartedAtUtc = now.AddHours(-1),
            LastSeenAtUtc = now,
            BootSessionId = "boot"
        }, CancellationToken.None);
        var editor = CreateEditor(games, sessions, now);

        await Assert.ThrowsAsync<YFTimeTrackerException>(() => editor.UpdateManualSessionAsync(
            session.Id,
            now.AddHours(-1),
            now,
            CancellationToken.None));
        await Assert.ThrowsAsync<YFTimeTrackerException>(() => editor.DeleteSessionAsync(
            session.Id,
            CancellationToken.None));

        Assert.IsNotNull(await sessions.GetByIdAsync(session.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task AddManualSessionAsync_rejects_future_end_time()
    {
        var now = DateTimeOffset.Parse("2026-08-30T12:00:00Z");
        var games = new InMemoryGameRepository();
        var game = await AddGameAsync(games, now);
        var sessions = new InMemoryGameSessionRepository(id => id == game.Id ? game : null);
        var editor = CreateEditor(games, sessions, now);

        await Assert.ThrowsAsync<YFTimeTrackerException>(() => editor.AddManualSessionAsync(
            game.Id,
            now,
            now.AddMinutes(1),
            CancellationToken.None));
    }

    private static GameSessionEditor CreateEditor(
        InMemoryGameRepository games,
        InMemoryGameSessionRepository sessions,
        DateTimeOffset now)
    {
        return new GameSessionEditor(
            games,
            sessions,
            new FakeBootSessionProvider("boot"),
            new FakeClock(now));
    }

    private static Task<Game> AddGameAsync(InMemoryGameRepository games, DateTimeOffset now)
    {
        return games.AddAsync(new Game
        {
            Name = "Test",
            ExecutablePath = @"C:\Games\Test.exe",
            ExecutablePathKey = @"C:\GAMES\TEST.EXE",
            ExecutableName = "Test.exe",
            AddedAtUtc = now
        }, CancellationToken.None);
    }
}
