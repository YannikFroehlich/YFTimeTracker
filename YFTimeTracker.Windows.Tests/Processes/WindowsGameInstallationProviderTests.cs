using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class WindowsGameInstallationProviderTests
{
    [TestMethod]
    public async Task Steam_app_is_discovered_in_its_common_folder_and_in_a_separately_downloaded_depot()
    {
        using var steamRoot = new TemporaryDirectory();
        var steamApps = Path.Combine(steamRoot.Path, "steamapps");
        Directory.CreateDirectory(Path.Combine(steamApps, "common", "BeamNG.drive"));
        Directory.CreateDirectory(Path.Combine(steamApps, "content", "app_284160", "depot_284161"));
        await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_284160.acf"), """
            "AppState"
            {
            	"appid"		"284160"
            	"name"		"BeamNG.drive"
            	"installdir"		"BeamNG.drive"
            }
            """);

        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            new FakeXboxPackageCatalog([]),
            new FakeDirectoryLinkResolver(string.Empty, string.Empty),
            () => steamRoot.Path);

        var result = await provider.DiscoverAsync(CancellationToken.None);

        var directories = result.Games
            .Where(game => game.Source == GameSource.Steam && game.ExternalGameId == "284160")
            .Select(game => game.InstallDirectory)
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.GetFullPath(Path.Combine(steamApps, "common", "BeamNG.drive")),
                Path.GetFullPath(Path.Combine(steamApps, "content", "app_284160"))
            },
            directories);
    }

    [TestMethod]
    public async Task Steam_app_without_a_downloaded_depot_is_discovered_only_once()
    {
        using var steamRoot = new TemporaryDirectory();
        var steamApps = Path.Combine(steamRoot.Path, "steamapps");
        Directory.CreateDirectory(Path.Combine(steamApps, "common", "NeonGame"));
        await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_42.acf"), """
            "AppState"
            {
            	"appid"		"42"
            	"name"		"Neon Game"
            	"installdir"		"NeonGame"
            }
            """);

        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            new FakeXboxPackageCatalog([]),
            new FakeDirectoryLinkResolver(string.Empty, string.Empty),
            () => steamRoot.Path);

        var result = await provider.DiscoverAsync(CancellationToken.None);

        var game = result.Games.Single(game => game.Source == GameSource.Steam);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(steamApps, "common", "NeonGame")), game.InstallDirectory);
    }

    [TestMethod]
    public async Task Ea_app_game_is_discovered_from_local_installation_catalog()
    {
        using var directory = new TemporaryDirectory();
        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            new FakeXboxPackageCatalog([]),
            new FakeDirectoryLinkResolver(string.Empty, string.Empty),
            () => null,
            new FakeEaInstallationCatalog(new EaInstallationCatalogResult(
                true,
                [new EaInstallationEntry("ea-fc", "EA SPORTS FC", directory.Path)])));

        var result = await provider.DiscoverAsync(CancellationToken.None);

        Assert.AreEqual(LauncherAvailability.Available, result.Sources[GameSource.EaApp]);
        var game = result.Games.Single(game => game.Source == GameSource.EaApp);
        Assert.AreEqual("ea-fc", game.ExternalGameId);
        Assert.AreEqual("EA SPORTS FC", game.Name);
        Assert.AreEqual(Path.GetFullPath(directory.Path), game.InstallDirectory);
        Assert.IsEmpty(game.LaunchExecutablePaths);
    }

    [TestMethod]
    public async Task Ea_app_registration_for_a_steam_directory_does_not_create_a_duplicate()
    {
        using var steamRoot = new TemporaryDirectory();
        var steamApps = Path.Combine(steamRoot.Path, "steamapps");
        var gameDirectory = Path.Combine(steamApps, "common", "EaGame");
        Directory.CreateDirectory(gameDirectory);
        await File.WriteAllTextAsync(Path.Combine(steamApps, "appmanifest_99.acf"), """
            "AppState"
            {
                "appid" "99"
                "name" "EA Game"
                "installdir" "EaGame"
            }
            """);
        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            new FakeXboxPackageCatalog([]),
            new FakeDirectoryLinkResolver(string.Empty, string.Empty),
            () => steamRoot.Path,
            new FakeEaInstallationCatalog(new EaInstallationCatalogResult(
                true,
                [new EaInstallationEntry("ea-game", "EA Game", gameDirectory)])));

        var result = await provider.DiscoverAsync(CancellationToken.None);

        Assert.AreEqual(LauncherAvailability.Available, result.Sources[GameSource.EaApp]);
        Assert.IsEmpty(result.Games.Where(game => game.Source == GameSource.EaApp));
        Assert.HasCount(1, result.Games.Where(game => game.Source == GameSource.Steam && game.ExternalGameId == "99"));
    }

    [TestMethod]
    public async Task Xbox_package_with_game_config_is_discovered_from_effective_location()
    {
        using var directory = new TemporaryDirectory();
        var contentDirectory = Path.Combine(directory.Path, "Content");
        Directory.CreateDirectory(Path.Combine(contentDirectory, "bin"));
        await File.WriteAllTextAsync(Path.Combine(contentDirectory, "MicrosoftGame.config"), """
            <Game configVersion="1">
              <Identity Name="Contoso.NeonGame" Publisher="CN=Contoso" Version="1.0.0.0" />
              <ExecutableList>
                <Executable Name="bin\NeonGame.exe" Id="Game" TargetDeviceFamily="PC" />
                <Executable Name="..\Outside.exe" Id="Invalid" TargetDeviceFamily="PC" />
              </ExecutableList>
              <ShellVisuals DefaultDisplayName="Neon Game aus Manifest" />
            </Game>
            """);

        var catalog = new FakeXboxPackageCatalog(
        [
            new XboxPackageInfo("Microsoft.GamingServices", "Microsoft.GamingServices_8wekyb3d8bbwe", "Gaming Services", null),
            new XboxPackageInfo("Contoso.NeonGame", "Contoso.NeonGame_123", "Neon Game", directory.Path)
        ]);
        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            catalog);

        var result = await provider.DiscoverAsync(CancellationToken.None);

        Assert.AreEqual(LauncherAvailability.Available, result.Sources[GameSource.Xbox]);
        var game = result.Games.Single(game => game.Source == GameSource.Xbox);
        Assert.AreEqual("Contoso.NeonGame_123", game.ExternalGameId);
        Assert.AreEqual("Neon Game", game.Name);
        Assert.AreEqual(Path.GetFullPath(contentDirectory), game.InstallDirectory);
        CollectionAssert.AreEqual(
            new[] { Path.GetFullPath(Path.Combine(contentDirectory, "bin", "NeonGame.exe")) },
            game.LaunchExecutablePaths.ToArray());
    }

    [TestMethod]
    public async Task Xbox_package_directory_that_is_a_junction_is_registered_under_its_link_target()
    {
        using var packageDirectory = new TemporaryDirectory();
        using var contentDirectory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(packageDirectory.Path, "MicrosoftGame.config"), """
            <Game configVersion="1">
              <Identity Name="Contoso.NeonGame" Publisher="CN=Contoso" Version="1.0.0.0" />
              <ExecutableList>
                <Executable Name="NeonGame.exe" Id="Game" TargetDeviceFamily="PC" />
              </ExecutableList>
              <ShellVisuals DefaultDisplayName="Neon Game" />
            </Game>
            """);

        var catalog = new FakeXboxPackageCatalog(
        [
            new XboxPackageInfo("Contoso.NeonGame", "Contoso.NeonGame_123", "Neon Game", packageDirectory.Path)
        ]);
        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            catalog,
            new FakeDirectoryLinkResolver(packageDirectory.Path, contentDirectory.Path),
            () => null);

        var result = await provider.DiscoverAsync(CancellationToken.None);

        var game = result.Games.Single(game => game.Source == GameSource.Xbox);
        Assert.AreEqual(Path.GetFullPath(contentDirectory.Path), game.InstallDirectory);
        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.GetFullPath(Path.Combine(contentDirectory.Path, "NeonGame.exe")),
                Path.GetFullPath(Path.Combine(packageDirectory.Path, "NeonGame.exe"))
            },
            game.LaunchExecutablePaths.ToArray());
    }

    [TestMethod]
    public async Task Broken_xbox_manifest_does_not_block_launcher_status_or_other_sources()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "MicrosoftGame.config"), "not xml");
        var catalog = new FakeXboxPackageCatalog(
        [
            new XboxPackageInfo("Microsoft.GamingApp", "Microsoft.GamingApp_8wekyb3d8bbwe", "Xbox", null),
            new XboxPackageInfo("Contoso.Broken", "Contoso.Broken_123", "Defektes Spiel", directory.Path)
        ]);
        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            catalog);

        var result = await provider.DiscoverAsync(CancellationToken.None);

        Assert.AreEqual(LauncherAvailability.Available, result.Sources[GameSource.Xbox]);
        Assert.IsEmpty(result.Games.Where(game => game.Source == GameSource.Xbox));
    }

    [TestMethod]
    public async Task Xbox_package_catalog_error_is_reported_without_throwing()
    {
        var provider = new WindowsGameInstallationProvider(
            NullLogger<WindowsGameInstallationProvider>.Instance,
            new FakeXboxPackageCatalog(new UnauthorizedAccessException("Kein Zugriff")));

        var result = await provider.DiscoverAsync(CancellationToken.None);

        Assert.AreEqual(LauncherAvailability.Error, result.Sources[GameSource.Xbox]);
    }

    private sealed class FakeDirectoryLinkResolver(string linkDirectory, string targetDirectory) : IDirectoryLinkResolver
    {
        public string ResolveFinalTarget(string directory) =>
            string.Equals(directory, linkDirectory, StringComparison.OrdinalIgnoreCase) ? targetDirectory : directory;
    }

    private sealed class FakeEaInstallationCatalog(EaInstallationCatalogResult result) : IEaInstallationCatalog
    {
        public EaInstallationCatalogResult GetInstallations() => result;
    }

    private sealed class FakeXboxPackageCatalog : IXboxPackageCatalog
    {
        private readonly IReadOnlyList<XboxPackageInfo> packages = [];
        private readonly Exception? exception;

        public FakeXboxPackageCatalog(IReadOnlyList<XboxPackageInfo> packages)
        {
            this.packages = packages;
        }

        public FakeXboxPackageCatalog(Exception exception)
        {
            this.exception = exception;
        }

        public IReadOnlyList<XboxPackageInfo> GetInstalledPackages() => exception is null
            ? packages
            : throw exception;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"YFTimeTracker-Xbox-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
