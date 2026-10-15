using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace YFTimeTracker.Windows.Processes;

internal static partial class LauncherManifestParsers
{
    internal sealed record SteamManifest(string AppId, string Name, string InstallDirectoryName);

    internal sealed record EpicManifest(string ExternalId, string Name, string InstallDirectory, string? LaunchExecutable);

    internal sealed record XboxManifest(string IdentityName, string? DisplayName, IReadOnlyList<string> LaunchExecutables);

    internal sealed record UninstallEntry(string? DisplayName, string? Publisher, string? InstallLocation, string? UninstallString);

    internal sealed record UninstallGame(string ExternalId, string Name, string InstallDirectory);

    internal sealed record UninstallCatalog(bool IsLauncherInstalled, IReadOnlyList<UninstallGame> Games);

    /// <summary>
    /// Riot Client: je Spiel ein Windows-Uninstall-Eintrag ("Riot Game valorant.live") mit
    /// <c>RiotClientServices.exe --uninstall-product=valorant --uninstall-patchline=live</c>.
    /// Der Riot Client selbst und Testserver (PBE) sind keine Spiele.
    /// </summary>
    public static UninstallCatalog ParseRiotInstallations(IEnumerable<UninstallEntry> entries)
    {
        var installed = false;
        var games = new List<UninstallGame>();
        foreach (var entry in entries)
        {
            var match = RiotUninstallRegex().Match(entry.UninstallString ?? string.Empty);
            if (!match.Success)
            {
                continue;
            }

            installed = true;
            var product = match.Groups["product"].Value;
            if (string.Equals(product, "Riot_Client", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(match.Groups["patchline"].Value, "live", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(entry.DisplayName)
                || string.IsNullOrWhiteSpace(entry.InstallLocation))
            {
                continue;
            }

            games.Add(new UninstallGame(product, entry.DisplayName, entry.InstallLocation));
        }

        return new UninstallCatalog(installed, games);
    }

    /// <summary>
    /// Rockstar Games Launcher: Spiele tragen sich mit <c>-uninstall=&lt;TitleId&gt;</c> ein, der
    /// Launcher selbst mit <c>-uninstall=launcher</c>. Über Steam oder Epic gekaufte Rockstar-Spiele
    /// haben keinen solchen Eintrag und bleiben bei ihrem Store.
    /// </summary>
    public static UninstallCatalog ParseRockstarInstallations(IEnumerable<UninstallEntry> entries)
    {
        var installed = false;
        var games = new List<UninstallGame>();
        foreach (var entry in entries)
        {
            var uninstallString = entry.UninstallString ?? string.Empty;
            var isRockstar = entry.Publisher?.Contains("Rockstar Games", StringComparison.OrdinalIgnoreCase) == true
                || uninstallString.Contains(@"Rockstar Games\Launcher\", StringComparison.OrdinalIgnoreCase);
            var match = RockstarUninstallRegex().Match(uninstallString);
            if (!isRockstar || !match.Success)
            {
                continue;
            }

            installed = true;
            var titleId = match.Groups["id"].Value;
            if (string.Equals(titleId, "launcher", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(entry.DisplayName)
                || string.IsNullOrWhiteSpace(entry.InstallLocation))
            {
                continue;
            }

            games.Add(new UninstallGame(titleId, entry.DisplayName, entry.InstallLocation));
        }

        return new UninstallCatalog(installed, games);
    }

    public static IReadOnlyList<string> ParseSteamLibraryPaths(string contents)
    {
        return VdfPathRegex().Matches(contents).Cast<Match>()
            .Select(match => match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static SteamManifest? ParseSteamManifest(string contents, string fallbackAppId)
    {
        var appId = ReadVdfValue(contents, "appid") ?? fallbackAppId;
        var name = ReadVdfValue(contents, "name");
        var installDirectory = ReadVdfValue(contents, "installdir");
        return string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory)
            ? null
            : new SteamManifest(appId, name, installDirectory);
    }

    /// <summary>
    /// Liest aus Steams <c>localconfig.vdf</c> die Spielzeit je AppId in Minuten
    /// (<c>Software\Valve\Steam\apps\&lt;AppId&gt;\Playtime</c>). Ein App-Block kann
    /// verschachtelte Blöcke wie <c>cloud</c> enthalten, deshalb wird der Pfad
    /// mitgeführt statt per Regex gesucht.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ParseSteamPlaytimes(string contents)
    {
        var playtimes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (path, key, value) in EnumerateVdfValues(contents))
        {
            if (path.Count >= 3
                && string.Equals(key, "Playtime", StringComparison.OrdinalIgnoreCase)
                && string.Equals(path[^2], "apps", StringComparison.OrdinalIgnoreCase)
                && string.Equals(path[^3], "Steam", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            {
                playtimes[path[^1]] = minutes;
            }
        }

        return playtimes;
    }

    /// <summary>
    /// Liest aus Steams <c>config\loginusers.vdf</c> das Konto mit <c>MostRecent</c> und gibt
    /// dessen Ordnernamen unter <c>userdata</c> zurück. Die Datei führt SteamID64-Werte,
    /// der Ordner heißt nach der 32-Bit-Konto-Id in deren unteren Bits.
    /// </summary>
    public static string? ParseSteamMostRecentAccountId(string contents)
    {
        foreach (var (path, key, value) in EnumerateVdfValues(contents))
        {
            if (path.Count == 2
                && string.Equals(path[0], "users", StringComparison.OrdinalIgnoreCase)
                && string.Equals(key, "MostRecent", StringComparison.OrdinalIgnoreCase)
                && value == "1"
                && ulong.TryParse(path[1], NumberStyles.None, CultureInfo.InvariantCulture, out var steamId64))
            {
                return (steamId64 & 0xFFFFFFFF).ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    public static EpicManifest? ParseEpicManifest(string contents)
    {
        using var document = JsonDocument.Parse(contents);
        var root = document.RootElement;
        var name = GetString(root, "DisplayName");
        var installDirectory = GetString(root, "InstallLocation");
        var externalId = GetString(root, "CatalogItemId") ?? GetString(root, "AppName");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory) || string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        return new EpicManifest(externalId, name, installDirectory, GetString(root, "LaunchExecutable"));
    }

    public static string? ResolveGogLaunchPath(string installDirectory, string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        string candidate;
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            candidate = closingQuote > 1 ? trimmed[1..closingQuote] : trimmed.Trim('"');
        }
        else
        {
            var exeEnd = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exeEnd >= 0 ? trimmed[..(exeEnd + 4)] : trimmed;
        }

        if (!string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.IsPathRooted(candidate) ? candidate : Path.Combine(installDirectory, candidate);
    }

    public static XboxManifest? ParseXboxGameConfig(string contents)
    {
        var document = XDocument.Parse(contents, LoadOptions.None);
        if (document.Root is null || !string.Equals(document.Root.Name.LocalName, "Game", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var identityName = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Identity", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Name")
            ?.Value;
        var displayName = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "ShellVisuals", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("DefaultDisplayName")
            ?.Value;
        var executables = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Executable", StringComparison.OrdinalIgnoreCase))
            .Where(element => !bool.TryParse(element.Attribute("IsDevOnly")?.Value, out var isDevOnly) || !isDevOnly)
            .Where(element =>
            {
                var target = element.Attribute("TargetDeviceFamily")?.Value;
                return string.IsNullOrWhiteSpace(target) || string.Equals(target, "PC", StringComparison.OrdinalIgnoreCase);
            })
            .Select(element => element.Attribute("Name")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path)
                && !Path.IsPathRooted(path)
                && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.IsNullOrWhiteSpace(identityName) || executables.Length == 0
            ? null
            : new XboxManifest(identityName.Trim(), displayName?.Trim(), executables);
    }

    /// <summary>
    /// Liefert jedes Schlüssel-Wert-Paar einer VDF-Datei mit dem Pfad der umschließenden
    /// Blöcke. Der Pfad ist nur bis zum nächsten Element gültig.
    /// </summary>
    private static IEnumerable<(IReadOnlyList<string> Path, string Key, string Value)> EnumerateVdfValues(string contents)
    {
        var path = new List<string>();
        string? key = null;
        foreach (Match token in VdfTokenRegex().Matches(contents))
        {
            switch (token.Value)
            {
                case "{":
                    path.Add(key ?? string.Empty);
                    key = null;
                    break;
                case "}":
                    if (path.Count > 0)
                    {
                        path.RemoveAt(path.Count - 1);
                    }

                    key = null;
                    break;
                default:
                    if (key is null)
                    {
                        key = token.Groups[1].Value;
                        break;
                    }

                    yield return (path, key, token.Groups[1].Value);
                    key = null;
                    break;
            }
        }
    }

    private static string? ReadVdfValue(string contents, string key)
    {
        var match = Regex.Match(contents, $"\\\"{Regex.Escape(key)}\\\"\\s+\\\"([^\\\"]*)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal) : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VdfPathRegex();

    [GeneratedRegex("\"((?:[^\"\\\\]|\\\\.)*)\"|[{}]", RegexOptions.CultureInvariant)]
    private static partial Regex VdfTokenRegex();

    [GeneratedRegex("RiotClientServices\\.exe\"?\\s.*?--uninstall-product=(?<product>[^\\s\"]+)(?:.*?--uninstall-patchline=(?<patchline>[^\\s\"]*))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RiotUninstallRegex();

    [GeneratedRegex("(?:^|\\s)-uninstall=(?<id>[^\\s\"]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RockstarUninstallRegex();
}
