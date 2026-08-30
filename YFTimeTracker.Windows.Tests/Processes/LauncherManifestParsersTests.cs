using System.Text.Json;
using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class LauncherManifestParsersTests
{
    [TestMethod]
    public void Steam_library_and_game_manifests_are_parsed()
    {
        const string libraries = """
            "libraryfolders"
            {
                "0" { "path" "C:\\Program Files (x86)\\Steam" }
                "1" { "path" "D:\\Games\\Steam" }
            }
            """;
        const string manifest = """
            "AppState"
            {
                "appid" "42"
                "name" "Neon Game"
                "installdir" "NeonGame"
            }
            """;

        var paths = LauncherManifestParsers.ParseSteamLibraryPaths(libraries);
        var game = LauncherManifestParsers.ParseSteamManifest(manifest, "fallback");

        CollectionAssert.AreEquivalent(new[] { @"C:\Program Files (x86)\Steam", @"D:\Games\Steam" }, paths.ToArray());
        Assert.IsNotNull(game);
        Assert.AreEqual("42", game.AppId);
        Assert.AreEqual("Neon Game", game.Name);
        Assert.AreEqual("NeonGame", game.InstallDirectoryName);
    }

    [TestMethod]
    public void Epic_manifest_requires_identity_name_and_install_location()
    {
        const string valid = """
            {
              "CatalogItemId": "catalog-1",
              "DisplayName": "Neon Game",
              "InstallLocation": "D:\\Epic\\NeonGame",
              "LaunchExecutable": "bin\\game.exe"
            }
            """;
        const string incomplete = "{ \"DisplayName\": \"Missing path\" }";

        var game = LauncherManifestParsers.ParseEpicManifest(valid);

        Assert.IsNotNull(game);
        Assert.AreEqual("catalog-1", game.ExternalId);
        Assert.AreEqual(@"bin\game.exe", game.LaunchExecutable);
        Assert.IsNull(LauncherManifestParsers.ParseEpicManifest(incomplete));
        Assert.Throws<JsonException>(() => LauncherManifestParsers.ParseEpicManifest("not json"));
    }

    [TestMethod]
    public void Gog_launch_commands_are_resolved_without_arguments()
    {
        var quoted = LauncherManifestParsers.ResolveGogLaunchPath(@"D:\GOG\Neon", "\"bin\\game.exe\" -windowed");
        var plain = LauncherManifestParsers.ResolveGogLaunchPath(@"D:\GOG\Neon", "game.exe -lang de");
        var invalid = LauncherManifestParsers.ResolveGogLaunchPath(@"D:\GOG\Neon", "game.bat");

        Assert.AreEqual(@"D:\GOG\Neon\bin\game.exe", quoted);
        Assert.AreEqual(@"D:\GOG\Neon\game.exe", plain);
        Assert.IsNull(invalid);
    }
}
