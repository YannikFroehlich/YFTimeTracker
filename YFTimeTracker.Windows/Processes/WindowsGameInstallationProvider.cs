using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Windows.Processes;

public sealed class WindowsGameInstallationProvider : IGameInstallationProvider
{
    private static readonly Regex BattleNetUidRegex = new(
        @"Battle\.net.*--uid=(?<uid>\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] UninstallRegistrySubKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private readonly ILogger<WindowsGameInstallationProvider> logger;
    private readonly IXboxPackageCatalog xboxPackages;

    public WindowsGameInstallationProvider(ILogger<WindowsGameInstallationProvider> logger)
        : this(logger, new WindowsXboxPackageCatalog())
    {
    }

    internal WindowsGameInstallationProvider(
        ILogger<WindowsGameInstallationProvider> logger,
        IXboxPackageCatalog xboxPackages)
    {
        this.logger = logger;
        this.xboxPackages = xboxPackages;
    }

    public Task<LauncherDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var games = new List<GameInstallationInfo>();
        var sources = new Dictionary<GameSource, LauncherAvailability>();

        DiscoverSafely(GameSource.Steam, () => DiscoverSteam(games, cancellationToken), sources);
        DiscoverSafely(GameSource.Epic, () => DiscoverEpic(games, cancellationToken), sources);
        DiscoverSafely(GameSource.Gog, () => DiscoverGog(games, cancellationToken), sources);
        DiscoverSafely(GameSource.Xbox, () => DiscoverXbox(games, cancellationToken), sources);
        DiscoverSafely(GameSource.BattleNet, () => DiscoverBattleNet(games, cancellationToken), sources);
        DiscoverSafely(GameSource.Ubisoft, () => DiscoverUbisoft(games, cancellationToken), sources);

        var distinctGames = games
            .GroupBy(game => (game.Source, game.ExternalGameId), new SourceIdComparer())
            .Select(group => group.First())
            .ToArray();

        return Task.FromResult(new LauncherDiscoveryResult(distinctGames, sources));
    }

    private void DiscoverSafely(
        GameSource source,
        Func<bool> discover,
        IDictionary<GameSource, LauncherAvailability> sources)
    {
        try
        {
            sources[source] = discover() ? LauncherAvailability.Available : LauncherAvailability.NotInstalled;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read {Launcher} game installations.", source);
            sources[source] = LauncherAvailability.Error;
        }
    }

    private static bool DiscoverSteam(ICollection<GameInstallationInfo> games, CancellationToken cancellationToken)
    {
        var steamRoot = ReadRegistryString(RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath")
            ?? ReadRegistryString(RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Valve\Steam", "InstallPath");
        if (string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot))
        {
            return false;
        }

        var libraryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
        var libraryConfig = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryConfig))
        {
            var contents = File.ReadAllText(libraryConfig);
            foreach (var path in LauncherManifestParsers.ParseSteamLibraryPaths(contents))
            {
                if (Directory.Exists(path))
                {
                    libraryRoots.Add(path);
                }
            }
        }

        foreach (var libraryRoot in libraryRoots)
        {
            var steamApps = Path.Combine(libraryRoot, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fallbackAppId = Path.GetFileNameWithoutExtension(manifestPath)["appmanifest_".Length..];
                var manifest = LauncherManifestParsers.ParseSteamManifest(File.ReadAllText(manifestPath), fallbackAppId);
                if (manifest is null)
                {
                    continue;
                }

                AddInstallation(games, GameSource.Steam, manifest.AppId, manifest.Name, Path.Combine(steamApps, "common", manifest.InstallDirectoryName), []);
            }
        }

        return true;
    }

    private static bool DiscoverEpic(ICollection<GameInstallationInfo> games, CancellationToken cancellationToken)
    {
        var manifestDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestDirectory))
        {
            return false;
        }

        foreach (var manifestPath in Directory.EnumerateFiles(manifestDirectory, "*.item"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LauncherManifestParsers.EpicManifest? manifest;
            try
            {
                manifest = LauncherManifestParsers.ParseEpicManifest(File.ReadAllText(manifestPath));
            }
            catch (JsonException)
            {
                continue;
            }
            if (manifest is null)
            {
                continue;
            }

            var launchPaths = string.IsNullOrWhiteSpace(manifest.LaunchExecutable)
                ? Array.Empty<string>()
                : [Path.IsPathRooted(manifest.LaunchExecutable) ? manifest.LaunchExecutable : Path.Combine(manifest.InstallDirectory, manifest.LaunchExecutable)];
            AddInstallation(games, GameSource.Epic, manifest.ExternalId, manifest.Name, manifest.InstallDirectory, launchPaths);
        }

        return true;
    }

    private static bool DiscoverGog(ICollection<GameInstallationInfo> games, CancellationToken cancellationToken)
    {
        var foundRoot = false;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var gamesKey = baseKey.OpenSubKey(@"SOFTWARE\GOG.com\Games");
                if (gamesKey is null)
                {
                    continue;
                }

                foundRoot = true;
                foreach (var externalId in gamesKey.GetSubKeyNames())
                {
                    using var gameKey = gamesKey.OpenSubKey(externalId);
                    var name = gameKey?.GetValue("gameName") as string;
                    var installDirectory = gameKey?.GetValue("path") as string;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory))
                    {
                        continue;
                    }

                    var executable = gameKey?.GetValue("exe") as string ?? gameKey?.GetValue("launchCommand") as string;
                    var launchPaths = LauncherManifestParsers.ResolveGogLaunchPath(installDirectory, executable) is { } path ? new[] { path } : [];
                    AddInstallation(games, GameSource.Gog, externalId, name, installDirectory, launchPaths);
                }
            }
        }

        return foundRoot;
    }

    private static bool DiscoverBattleNet(ICollection<GameInstallationInfo> games, CancellationToken cancellationToken)
    {
        var found = false;

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var subKey in UninstallRegistrySubKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var uninstallKey = baseKey.OpenSubKey(subKey);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var entryName in uninstallKey.GetSubKeyNames())
                {
                    using var entryKey = uninstallKey.OpenSubKey(entryName);
                    var uninstallString = entryKey?.GetValue("UninstallString") as string;
                    if (string.IsNullOrWhiteSpace(uninstallString))
                    {
                        continue;
                    }

                    var match = BattleNetUidRegex.Match(uninstallString);
                    if (!match.Success)
                    {
                        continue;
                    }

                    found = true;
                    var uid = match.Groups["uid"].Value.Trim();
                    if (string.Equals(uid, "battle.net", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var displayName = entryKey?.GetValue("DisplayName") as string;
                    var installLocation = entryKey?.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(displayName)
                        || string.IsNullOrWhiteSpace(installLocation)
                        || displayName.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
                        || displayName.EndsWith("Beta", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AddInstallation(games, GameSource.BattleNet, uid, displayName, installLocation, []);
                }
            }
        }

        return found;
    }

    private static bool DiscoverUbisoft(ICollection<GameInstallationInfo> games, CancellationToken cancellationToken)
    {
        using var installsKey = OpenUbisoftInstallsKey(RegistryView.Registry32) ?? OpenUbisoftInstallsKey(RegistryView.Registry64);
        if (installsKey is null)
        {
            return false;
        }

        foreach (var externalId in installsKey.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var gameKey = installsKey.OpenSubKey(externalId);
            var installDirectory = (gameKey?.GetValue("InstallDir") as string)?.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(installDirectory))
            {
                continue;
            }

            var name = Path.GetFileName(installDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            AddInstallation(games, GameSource.Ubisoft, externalId, name, installDirectory, []);
        }

        return true;
    }

    private static RegistryKey? OpenUbisoftInstallsKey(RegistryView view)
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        return baseKey.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
    }

    private bool DiscoverXbox(ICollection<GameInstallationInfo> games, CancellationToken cancellationToken)
    {
        var packages = xboxPackages.GetInstalledPackages();
        var xboxInstalled = packages.Any(package => IsXboxInfrastructurePackage(package.PackageName));
        var gameFound = false;

        foreach (var package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(package.EffectiveLocationPath)
                || !Directory.Exists(package.EffectiveLocationPath))
            {
                continue;
            }

            foreach (var configPath in GetXboxConfigPaths(package.EffectiveLocationPath))
            {
                try
                {
                    var manifest = LauncherManifestParsers.ParseXboxGameConfig(File.ReadAllText(configPath));
                    if (manifest is null)
                    {
                        continue;
                    }

                    var installDirectory = Path.GetDirectoryName(configPath)!;
                    var launchPaths = manifest.LaunchExecutables
                        .Select(path => ResolveXboxLaunchPath(installDirectory, path))
                        .Where(path => path is not null)
                        .Select(path => path!)
                        .ToArray();
                    if (launchPaths.Length == 0)
                    {
                        continue;
                    }

                    AddInstallation(
                        games,
                        GameSource.Xbox,
                        string.IsNullOrWhiteSpace(package.PackageFamilyName) ? manifest.IdentityName : package.PackageFamilyName,
                        ResolveXboxDisplayName(package.DisplayName, manifest.DisplayName, manifest.IdentityName),
                        installDirectory,
                        launchPaths);
                    gameFound = true;
                    break;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
                {
                    logger.LogDebug(exception, "Could not read Xbox game manifest {ManifestPath}.", configPath);
                }
            }
        }

        return xboxInstalled || gameFound;
    }

    private static IEnumerable<string> GetXboxConfigPaths(string effectiveLocationPath)
    {
        var rootConfig = Path.Combine(effectiveLocationPath, "MicrosoftGame.config");
        if (File.Exists(rootConfig))
        {
            yield return rootConfig;
        }

        var contentConfig = Path.Combine(effectiveLocationPath, "Content", "MicrosoftGame.config");
        if (File.Exists(contentConfig))
        {
            yield return contentConfig;
        }
    }

    private static string? ResolveXboxLaunchPath(string installDirectory, string relativePath)
    {
        try
        {
            var normalizedDirectory = ExecutablePathNormalizer.NormalizePath(installDirectory);
            var normalizedPath = ExecutablePathNormalizer.NormalizePath(Path.Combine(normalizedDirectory, relativePath));
            var directoryPrefix = normalizedDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase)
                ? normalizedPath
                : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static string ResolveXboxDisplayName(string? packageDisplayName, string? manifestDisplayName, string identityName)
    {
        return new[] { packageDisplayName, manifestDisplayName, identityName }
            .First(candidate => !string.IsNullOrWhiteSpace(candidate) && !candidate.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))!
            .Trim();
    }

    private static bool IsXboxInfrastructurePackage(string packageName) =>
        string.Equals(packageName, "Microsoft.GamingApp", StringComparison.OrdinalIgnoreCase)
        || string.Equals(packageName, "Microsoft.GamingServices", StringComparison.OrdinalIgnoreCase)
        || string.Equals(packageName, "Microsoft.XboxApp", StringComparison.OrdinalIgnoreCase);

    private static void AddInstallation(
        ICollection<GameInstallationInfo> games,
        GameSource source,
        string externalId,
        string name,
        string installDirectory,
        IEnumerable<string> launchPaths)
    {
        if (!Directory.Exists(installDirectory))
        {
            return;
        }

        var normalizedDirectory = ExecutablePathNormalizer.NormalizePath(installDirectory);
        var normalizedLaunchPaths = launchPaths
            .Where(path => string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            .Select(ExecutablePathNormalizer.NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        games.Add(new GameInstallationInfo(
            source,
            externalId.Trim(),
            name.Trim(),
            normalizedDirectory,
            ExecutablePathNormalizer.CreateKey(normalizedDirectory),
            normalizedLaunchPaths));
    }

    private static string? ReadRegistryString(RegistryHive hive, RegistryView view, string subKey, string valueName)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(subKey);
        return key?.GetValue(valueName) as string;
    }

    private sealed class SourceIdComparer : IEqualityComparer<(GameSource Source, string ExternalGameId)>
    {
        public bool Equals((GameSource Source, string ExternalGameId) x, (GameSource Source, string ExternalGameId) y) =>
            x.Source == y.Source && string.Equals(x.ExternalGameId, y.ExternalGameId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((GameSource Source, string ExternalGameId) value) =>
            HashCode.Combine(value.Source, StringComparer.OrdinalIgnoreCase.GetHashCode(value.ExternalGameId));
    }
}
