using System.Text.Json;
using System.Xml;
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

    [TestMethod]
    public void Xbox_game_config_reads_pc_executables_and_ignores_dev_or_console_entries()
    {
        const string config = """
            <?xml version="1.0" encoding="utf-8"?>
            <Game configVersion="1">
              <Identity Name="Contoso.NeonGame" Publisher="CN=Contoso" Version="1.0.0.0" />
              <ExecutableList>
                <Executable Name="NeonGame.exe" Id="Game" TargetDeviceFamily="PC" />
                <Executable Name="bin\Renderer.exe" Id="Renderer" />
                <Executable Name="Tools\Editor.exe" IsDevOnly="true" />
                <Executable Name="Console.exe" TargetDeviceFamily="Scarlett" />
                <Executable Name="readme.txt" />
              </ExecutableList>
              <ShellVisuals DefaultDisplayName="Neon Game" />
            </Game>
            """;

        var game = LauncherManifestParsers.ParseXboxGameConfig(config);

        Assert.IsNotNull(game);
        Assert.AreEqual("Contoso.NeonGame", game.IdentityName);
        Assert.AreEqual("Neon Game", game.DisplayName);
        CollectionAssert.AreEqual(new[] { "NeonGame.exe", @"bin\Renderer.exe" }, game.LaunchExecutables.ToArray());
    }

    [TestMethod]
    public void Xbox_game_config_requires_identity_and_launch_executable()
    {
        const string missingIdentity = "<Game><ExecutableList><Executable Name=\"game.exe\" /></ExecutableList></Game>";
        const string missingExecutable = "<Game><Identity Name=\"Contoso.Empty\" /></Game>";

        Assert.IsNull(LauncherManifestParsers.ParseXboxGameConfig(missingIdentity));
        Assert.IsNull(LauncherManifestParsers.ParseXboxGameConfig(missingExecutable));
        Assert.Throws<XmlException>(() => LauncherManifestParsers.ParseXboxGameConfig("not xml"));
    }
}
