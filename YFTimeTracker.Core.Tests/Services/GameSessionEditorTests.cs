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
        var editor = new GameSessionEditor(games, sessions, new FakeBootSessionProvider("boot"));

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
}
