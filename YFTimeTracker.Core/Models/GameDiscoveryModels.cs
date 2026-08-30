namespace YFTimeTracker.Core.Models;

public sealed record RunningProcessInfo(
    string ExecutablePath,
    string ExecutablePathKey,
    DateTimeOffset? StartedAtUtc = null);

public sealed record GameInstallationInfo(
    GameSource Source,
    string ExternalGameId,
    string Name,
    string InstallDirectory,
    string InstallDirectoryKey,
    IReadOnlyList<string> LaunchExecutablePaths);

public enum LauncherAvailability
{
    NotInstalled,
    Available,
    Error
}

public sealed record LauncherDiscoveryResult(
    IReadOnlyList<GameInstallationInfo> Games,
    IReadOnlyDictionary<GameSource, LauncherAvailability> Sources)
{
    public static LauncherDiscoveryResult Empty { get; } = new(
        [],
        new Dictionary<GameSource, LauncherAvailability>
        {
            [GameSource.Steam] = LauncherAvailability.NotInstalled,
            [GameSource.Epic] = LauncherAvailability.NotInstalled,
            [GameSource.Gog] = LauncherAvailability.NotInstalled,
            [GameSource.Xbox] = LauncherAvailability.NotInstalled
        });
}
