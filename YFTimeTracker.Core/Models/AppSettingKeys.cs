namespace YFTimeTracker.Core.Models;

public static class AppSettingKeys
{
    public const string TrackingEnabled = "tracking.enabled";
    public const string LauncherDiscoveryEnabled = "tracking.launcherDiscoveryEnabled";
    public const string TrackingIntervalSeconds = "tracking.intervalSeconds";
    public const string HeartbeatIntervalSeconds = "tracking.heartbeatIntervalSeconds";
    public const string MinimizeOnClose = "ui.minimizeOnClose";
    public const string StartMinimized = "ui.startMinimized";
    public const string FirstRunSetupCompleted = "ui.firstRunSetupCompleted";
    public const string BackupRetentionDays = "backup.retentionDays";
    public const string StartupEnabled = "windows.startupEnabled";
    public const string LastBackupDate = "backup.lastBackupDate";
    public const string Theme = "ui.theme";
    public const string LastSeenChangelogHeading = "ui.lastSeenChangelogHeading";
    public const string ProfileDisplayName = "profile.displayName";
    public const string ProfileAccentColor = "profile.accentColor";
}
