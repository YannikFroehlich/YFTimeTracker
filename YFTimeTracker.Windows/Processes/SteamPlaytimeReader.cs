using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Processes;

/// <summary>
/// Liest die Spielzeit aus <c>userdata\&lt;Konto&gt;\config\localconfig.vdf</c> des
/// Steam-Clients. Rein lokal, ohne Steam-Anmeldung oder Web-API.
/// </summary>
public sealed class SteamPlaytimeReader : ILauncherPlaytimeReader
{
    private readonly Func<string?> steamRootLocator;

    public SteamPlaytimeReader()
        : this(WindowsGameInstallationProvider.LocateSteamRoot)
    {
    }

    internal SteamPlaytimeReader(Func<string?> steamRootLocator)
    {
        this.steamRootLocator = steamRootLocator;
    }

    public GameSource Source => GameSource.Steam;

    public async Task<int?> GetPlaytimeMinutesAsync(string appId, CancellationToken cancellationToken)
    {
        var steamRoot = steamRootLocator();
        if (string.IsNullOrWhiteSpace(steamRoot))
        {
            return null;
        }

        var userData = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userData))
        {
            return null;
        }

        // loginusers.vdf nennt das zuletzt angemeldete Konto. Fehlt sie oder dessen Ordner,
        // gilt die zuletzt geschriebene localconfig.vdf als dessen Konfiguration.
        var loginUsers = await ReadSharedAsync(Path.Combine(steamRoot, "config", "loginusers.vdf"), cancellationToken);
        var accountId = loginUsers is null ? null : LauncherManifestParsers.ParseSteamMostRecentAccountId(loginUsers);
        var config = accountId is null ? null : new FileInfo(Path.Combine(userData, accountId, "config", "localconfig.vdf"));
        if (config is not { Exists: true })
        {
            config = Directory.EnumerateDirectories(userData)
                .Select(directory => new FileInfo(Path.Combine(directory, "config", "localconfig.vdf")))
                .Where(file => file.Exists)
                .MaxBy(file => file.LastWriteTimeUtc);
        }

        var contents = config is null ? null : await ReadSharedAsync(config.FullName, cancellationToken);
        return contents is not null && LauncherManifestParsers.ParseSteamPlaytimes(contents).TryGetValue(appId, out var minutes)
            ? minutes
            : null;
    }

    private static async Task<string?> ReadSharedAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // Steam hält seine Dateien während des Betriebs offen.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
