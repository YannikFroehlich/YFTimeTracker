using System.Text.Json;
using System.Text.RegularExpressions;

namespace YFTimeTracker.Windows.Processes;

internal static partial class LauncherManifestParsers
{
    internal sealed record SteamManifest(string AppId, string Name, string InstallDirectoryName);

    internal sealed record EpicManifest(string ExternalId, string Name, string InstallDirectory, string? LaunchExecutable);

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
}
