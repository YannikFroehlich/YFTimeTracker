using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;
using YFTimeTracker.Core.Services;

namespace YFTimeTracker.Windows.Processes;

public sealed class WindowsGameInstallationProvider(ILogger<WindowsGameInstallationProvider> logger) : IGameInstallationProvider
{
    public Task<LauncherDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken)
    {
        var games = new List<GameInstallationInfo>();
        var sources = new Dictionary<GameSource, LauncherAvailability>();

        DiscoverSafely(GameSource.Steam, () => DiscoverSteam(games, cancellationToken), sources);
        DiscoverSafely(GameSource.Epic, () => DiscoverEpic(games, cancellationToken), sources);
        DiscoverSafely(GameSource.Gog, () => DiscoverGog(games, cancellationToken), sources);

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
