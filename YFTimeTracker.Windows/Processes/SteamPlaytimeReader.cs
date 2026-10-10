using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.Windows.Processes;

/// <summary>
/// Liest die Spielzeit aus <c>userdata\&lt;Konto&gt;\config\localconfig.vdf</c> des
/// Steam-Clients. Rein lokal, ohne Steam-Anmeldung oder Web-API.
/// </summary>
public sealed class SteamPlaytimeReader : ISteamPlaytimeReader
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

    public async Task<int?> GetPlaytimeMinutesAsync(string appId, CancellationToken cancellationToken)
    {
        var steamRoot = steamRootLocator();
        var userData = string.IsNullOrWhiteSpace(steamRoot) ? null : Path.Combine(steamRoot, "userdata");
        if (userData is null || !Directory.Exists(userData))
        {
            return null;
        }

        // ponytail: zuletzt geschriebene localconfig.vdf = zuletzt angemeldetes Steam-Konto.
        // Teilen sich mehrere Personen einen PC, wäre loginusers.vdf ("MostRecent") genauer.
        var config = Directory.EnumerateDirectories(userData)
            .Select(directory => new FileInfo(Path.Combine(directory, "config", "localconfig.vdf")))
            .Where(file => file.Exists)
            .MaxBy(file => file.LastWriteTimeUtc);
        if (config is null)
        {
            return null;
        }

        // Steam hält die Datei während des Betriebs offen.
        await using var stream = new FileStream(config.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var contents = await reader.ReadToEndAsync(cancellationToken);
        return LauncherManifestParsers.ParseSteamPlaytimes(contents).TryGetValue(appId, out var minutes) ? minutes : null;
    }
}
