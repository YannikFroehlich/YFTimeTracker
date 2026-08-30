using Microsoft.Extensions.Logging.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class WindowsGameInstallationProviderTests
{
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
