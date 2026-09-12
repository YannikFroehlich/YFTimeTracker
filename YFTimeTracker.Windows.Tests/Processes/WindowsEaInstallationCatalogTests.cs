using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class WindowsEaInstallationCatalogTests
{
    [TestMethod]
    public void Catalog_detects_launcher_and_keeps_only_installed_ea_games()
    {
        var installedDirectory = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-EA-{Guid.NewGuid():N}");
        Directory.CreateDirectory(installedDirectory);
        try
        {
            var missingDirectory = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-EA-Missing-{Guid.NewGuid():N}");
            var catalog = new WindowsEaInstallationCatalog(() =>
            [
                new EaRegistryEntry("ea-app", "EA app", "Electronic Arts, Inc.", null),
                new EaRegistryEntry("fc-26", "EA SPORTS FC 26", "Electronic Arts", installedDirectory),
                new EaRegistryEntry("fc-26-addon", "EA SPORTS FC 26 Zusatzinhalt", "Electronic Arts", installedDirectory),
                new EaRegistryEntry("anticheat", "EA AntiCheat", "Electronic Arts", installedDirectory),
                new EaRegistryEntry("stale", "Altes EA-Spiel", "Electronic Arts", missingDirectory),
                new EaRegistryEntry("other", "Anderes Programm", "Contoso", installedDirectory)
            ]);

            var result = catalog.GetInstallations();

            Assert.IsTrue(result.IsLauncherInstalled);
            var game = result.Games.Single();
            Assert.AreEqual("fc-26", game.ExternalId);
            Assert.AreEqual("EA SPORTS FC 26", game.Name);
            Assert.AreEqual(installedDirectory, game.InstallDirectory);
        }
        finally
        {
            Directory.Delete(installedDirectory, true);
        }
    }

    [TestMethod]
    public void Installed_ea_game_marks_launcher_available_without_separate_launcher_entry()
    {
        var installedDirectory = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-EA-{Guid.NewGuid():N}");
        Directory.CreateDirectory(installedDirectory);
        try
        {
            var catalog = new WindowsEaInstallationCatalog(() =>
            [
                new EaRegistryEntry("game", "EA-Spiel", "Electronic Arts", installedDirectory)
            ]);

            var result = catalog.GetInstallations();

            Assert.IsTrue(result.IsLauncherInstalled);
            Assert.HasCount(1, result.Games);
        }
        finally
        {
            Directory.Delete(installedDirectory, true);
        }
    }
}
