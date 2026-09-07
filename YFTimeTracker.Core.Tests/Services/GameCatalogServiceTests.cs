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
        var catalog = CreateCatalog();

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.AddGameAsync("   ", "Alpha", CancellationToken.None));

        Assert.AreEqual("Bitte wähle eine .exe-Datei aus.", exception.Message);
    }

    [TestMethod]
    public async Task Adding_a_game_with_a_non_executable_reports_a_german_message()
    {
        var catalog = CreateCatalog();

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.AddGameAsync(@"C:\Games\readme.txt", "Alpha", CancellationToken.None));

        Assert.AreEqual("Bitte wähle eine .exe-Datei aus.", exception.Message);
    }

    [TestMethod]
    public async Task Updating_a_game_without_a_display_name_reports_a_german_message()
    {
        var catalog = CreateCatalog();
        var game = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.UpdateGameAsync(
                game.Id,
                "  ",
                @"C:\Games\Alpha.exe",
                null,
                null,
                CancellationToken.None));

        Assert.AreEqual("Bitte gib einen Anzeigenamen an.", exception.Message);
    }

    [TestMethod]
    public async Task Updating_a_game_without_an_executable_reports_a_german_message()
    {
        var catalog = CreateCatalog();
        var game = await catalog.AddGameAsync(@"C:\Games\Alpha.exe", "Alpha", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<YFTimeTrackerException>(
            () => catalog.UpdateGameAsync(game.Id, "Alpha", string.Empty, null, null, CancellationToken.None));

        Assert.AreEqual("Bitte wähle eine .exe-Datei aus.", exception.Message);
    }

    private static GameCatalogService CreateCatalog()
    {
        return new GameCatalogService(
            new InMemoryGameRepository(),
            new FakeClock(DateTimeOffset.Parse("2026-09-07T12:00:00Z")));
    }
}
