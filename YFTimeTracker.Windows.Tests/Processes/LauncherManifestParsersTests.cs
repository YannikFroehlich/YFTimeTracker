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
    public void Steam_playtimes_are_read_per_app_even_after_nested_blocks()
    {
        const string localConfig = """
            "UserLocalConfigStore"
            {
                "friends" { "Playtime" "999" }
                "Software"
                {
                    "Valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "440"
                                {
                                    "cloud" { "last_sync_state" "synchronized" }
                                    "LastPlayed" "1649896107"
                                    "Playtime" "701"
                                }
                                "480" { "Playtime2wks" "5" "Playtime" "51" }
                                "730" { "LastPlayed" "1723996411" }
                                "999" { "Playtime" "kaputt" }
                            }
                        }
                    }
                }
            }
            """;

        var playtimes = LauncherManifestParsers.ParseSteamPlaytimes(localConfig);

        Assert.HasCount(2, playtimes);
        Assert.AreEqual(701, playtimes["440"]);
        Assert.AreEqual(51, playtimes["480"], "Playtime2wks ist nur die Zeit der letzten zwei Wochen.");
    }

    [TestMethod]
    public void Riot_games_come_from_live_uninstall_entries_without_the_client_itself()
    {
        LauncherManifestParsers.UninstallEntry[] entries =
        [
            new("Riot Client ", "Riot Games, Inc", "C:/Riot Games/Riot Client",
                "\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=Riot_Client --uninstall-patchline="),
            new("VALORANT", "Riot Games, Inc", "C:/Riot Games/VALORANT/live",
                "\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=valorant --uninstall-patchline=live"),
            new("League of Legends (PBE)", "Riot Games, Inc", "C:/Riot Games/League of Legends (PBE)",
                "\"C:\\Riot Games\\Riot Client\\RiotClientServices.exe\" --uninstall-product=league_of_legends --uninstall-patchline=pbe"),
            new("Riot Vanguard", "Riot Games, Inc.", @"C:\Program Files\Riot Vanguard",
                "\"C:\\Program Files\\Riot Vanguard\\installer.exe\"")
        ];

        var catalog = LauncherManifestParsers.ParseRiotInstallations(entries);

        Assert.IsTrue(catalog.IsLauncherInstalled);
        var game = catalog.Games.Single();
        Assert.AreEqual("valorant", game.ExternalId);
        Assert.AreEqual("VALORANT", game.Name);
        Assert.AreEqual("C:/Riot Games/VALORANT/live", game.InstallDirectory);
        Assert.IsFalse(LauncherManifestParsers.ParseRiotInstallations([entries[3]]).IsLauncherInstalled, "Vanguard allein ist kein Riot Client.");
    }

    [TestMethod]
    public void Rockstar_games_come_from_launcher_uninstall_entries_only()
    {
        LauncherManifestParsers.UninstallEntry[] entries =
        [
            new("Rockstar Games Launcher", "Rockstar Games", @"C:\Program Files\Rockstar Games\Launcher",
                "\"C:\\Program Files\\Rockstar Games\\Launcher\\uninstall.exe\" -enableFullMode -uninstall=launcher"),
            new("Red Dead Redemption 2", "Rockstar Games", @"C:\Program Files\Rockstar Games\Red Dead Redemption 2",
                "\"C:\\Program Files\\Rockstar Games\\Launcher\\uninstall.exe\" -uninstall=rdr2"),
            new("Rockstar Games SDK", "Rockstar Games", null,
                @"C:\Program Files\Rockstar Games\Social Club\uninstallRGSCRedistributable.exe"),
            new("Grand Theft Auto V Enhanced", "Rockstar North", @"C:\Steam\steamapps\common\Grand Theft Auto V Enhanced",
                "\"C:\\Program Files (x86)\\Steam\\steam.exe\" steam://uninstall/3240220"),
            new("Other Tool", "Someone Else", @"C:\Tools\Other", @"C:\Tools\Other\setup.exe -uninstall=tool")
        ];

        var catalog = LauncherManifestParsers.ParseRockstarInstallations(entries);

        Assert.IsTrue(catalog.IsLauncherInstalled);
        var game = catalog.Games.Single();
        Assert.AreEqual("rdr2", game.ExternalId);
        Assert.AreEqual("Red Dead Redemption 2", game.Name);
        Assert.IsFalse(LauncherManifestParsers.ParseRockstarInstallations(entries[2..]).IsLauncherInstalled);
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

    [TestMethod]
    public void Amazon_launch_command_comes_from_fuel_json()
    {
        const string fuel = """
            { "SchemaVersion": "2", "Main": { "Command": "Binaries\\Game.exe", "Args": ["-windowed"] } }
            """;

        Assert.AreEqual(@"Binaries\Game.exe", LauncherManifestParsers.ParseAmazonLaunchCommand(fuel));
        Assert.IsNull(LauncherManifestParsers.ParseAmazonLaunchCommand("""{ "SchemaVersion": "2" }"""));
        Assert.IsNull(LauncherManifestParsers.ParseAmazonLaunchCommand("kaputt"), "Eine kaputte fuel.json kostet nur die Start-EXE.");
    }

    [TestMethod]
    public void Itch_launch_paths_come_from_verdict_candidates()
    {
        const string verdict = """
            {
              "basePath": "C:\\itch\\apps\\neon",
              "totalSize": 1234,
              "candidates": [
                { "path": "Neon.exe", "flavor": "windows", "arch": "amd64" },
                { "path": "tools\\Editor.exe", "flavor": "windows" }
              ]
            }
            """;

        CollectionAssert.AreEqual(
            new[] { @"C:\itch\apps\neon\Neon.exe", @"C:\itch\apps\neon\tools\Editor.exe" },
            LauncherManifestParsers.ParseItchLaunchPaths(verdict, @"D:\fallback").ToArray());
        CollectionAssert.AreEqual(
            new[] { @"D:\fallback\Neon.exe" },
            LauncherManifestParsers.ParseItchLaunchPaths("""{ "candidates": [{ "path": "Neon.exe" }] }""", @"D:\fallback").ToArray(),
            "Ohne basePath gilt der Installationsordner.");
        Assert.IsEmpty(LauncherManifestParsers.ParseItchLaunchPaths(null, @"D:\fallback"));
        Assert.IsEmpty(LauncherManifestParsers.ParseItchLaunchPaths("kaputt", @"D:\fallback"));
    }

    [TestMethod]
    public void Humble_installations_are_the_downloaded_entries_of_the_collection()
    {
        const string config = """
            {
              "settings": { "downloadLocation": "D:\\Humble" },
              "game-collection-4": [
                { "machineName": "neon_windows", "gameName": "Neon", "status": "downloaded",
                  "filePath": "D:\\Humble\\Neon", "executablePath": "bin\\Neon.exe" },
                { "machineName": "old_windows", "gameName": "Old Game", "status": "installed",
                  "downloadFilePath": "D:\\Humble", "executablePath": "Old.exe" },
                { "machineName": "owned_windows", "gameName": "Nur gekauft", "status": "available" },
                { "machineName": "noexe_windows", "gameName": "Ohne EXE", "status": "downloaded", "filePath": "D:\\Humble\\NoExe" }
              ]
            }
            """;

        var games = LauncherManifestParsers.ParseHumbleInstallations(config);

        Assert.HasCount(3, games);
        Assert.AreEqual(new LauncherManifestParsers.HumbleInstallation("neon_windows", "Neon", @"D:\Humble\Neon", @"D:\Humble\Neon\bin\Neon.exe"), games[0]);
        Assert.AreEqual(new LauncherManifestParsers.HumbleInstallation("old_windows", "Old Game", @"D:\Humble\old_windows", @"D:\Humble\old_windows\Old.exe"), games[1]);
        Assert.IsNull(games[2].ExecutablePath);
        Assert.IsEmpty(LauncherManifestParsers.ParseHumbleInstallations("""{ "settings": {} }"""));
    }
}
