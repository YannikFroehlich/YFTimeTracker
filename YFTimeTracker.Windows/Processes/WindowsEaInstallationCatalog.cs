using Microsoft.Win32;

namespace YFTimeTracker.Windows.Processes;

internal sealed record EaInstallationCatalogResult(
    bool IsLauncherInstalled,
    IReadOnlyList<EaInstallationEntry> Games);

internal sealed record EaInstallationEntry(
    string ExternalId,
    string Name,
    string InstallDirectory);

internal sealed record EaRegistryEntry(
    string ExternalId,
    string? DisplayName,
    string? Publisher,
    string? InstallDirectory);

internal interface IEaInstallationCatalog
{
    EaInstallationCatalogResult GetInstallations();
}

internal sealed class WindowsEaInstallationCatalog : IEaInstallationCatalog
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private readonly Func<IReadOnlyList<EaRegistryEntry>> readRegistryEntries;

    public WindowsEaInstallationCatalog()
        : this(ReadRegistryEntries)
    {
    }

    internal WindowsEaInstallationCatalog(Func<IReadOnlyList<EaRegistryEntry>> readRegistryEntries)
    {
        this.readRegistryEntries = readRegistryEntries;
    }

    public EaInstallationCatalogResult GetInstallations()
    {
        var launcherInstalled = false;
        var games = new List<EaInstallationEntry>();

        foreach (var entry in readRegistryEntries())
        {
            if (IsEaLauncher(entry.DisplayName, entry.Publisher))
            {
                launcherInstalled = true;
                continue;
            }

            if (!IsEaGamePublisher(entry.Publisher)
                || string.IsNullOrWhiteSpace(entry.DisplayName)
                || IsEaAuxiliaryProduct(entry.DisplayName)
                || string.IsNullOrWhiteSpace(entry.InstallDirectory)
                || !Directory.Exists(entry.InstallDirectory))
            {
                continue;
            }

            games.Add(new EaInstallationEntry(entry.ExternalId, entry.DisplayName, entry.InstallDirectory));
        }

        return new EaInstallationCatalogResult(
            launcherInstalled || games.Count > 0,
            games
                .DistinctBy(
                    game => Path.GetFullPath(game.InstallDirectory),
                    StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IReadOnlyList<EaRegistryEntry> ReadRegistryEntries()
    {
        var entries = new List<EaRegistryEntry>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(UninstallSubKey);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var entryName in uninstallKey.GetSubKeyNames())
                {
                    using var entryKey = uninstallKey.OpenSubKey(entryName);
                    entries.Add(new EaRegistryEntry(
                        entryName,
                        entryKey?.GetValue("DisplayName") as string,
                        entryKey?.GetValue("Publisher") as string,
                        entryKey?.GetValue("InstallLocation") as string));
                }
            }
        }

        return entries;
    }

    private static bool IsEaLauncher(string? displayName, string? publisher) =>
        IsEaGamePublisher(publisher)
        && (string.Equals(displayName, "EA app", StringComparison.OrdinalIgnoreCase)
            || string.Equals(displayName, "EA Desktop", StringComparison.OrdinalIgnoreCase));

    private static bool IsEaGamePublisher(string? publisher) =>
        !string.IsNullOrWhiteSpace(publisher)
        && (string.Equals(publisher.Trim(), "Electronic Arts", StringComparison.OrdinalIgnoreCase)
            || publisher.Trim().StartsWith("Electronic Arts ", StringComparison.OrdinalIgnoreCase)
            || publisher.Trim().StartsWith("Electronic Arts,", StringComparison.OrdinalIgnoreCase));

    private static bool IsEaAuxiliaryProduct(string displayName) =>
        string.Equals(displayName, "Origin", StringComparison.OrdinalIgnoreCase)
        || displayName.StartsWith("EA app", StringComparison.OrdinalIgnoreCase)
        || displayName.StartsWith("EA Desktop", StringComparison.OrdinalIgnoreCase)
        || displayName.Contains("AntiCheat", StringComparison.OrdinalIgnoreCase)
        || displayName.StartsWith("EA Background", StringComparison.OrdinalIgnoreCase)
        || displayName.StartsWith("EA Error Reporter", StringComparison.OrdinalIgnoreCase);
}
