namespace YFTimeTracker.Core.Models;

public static class AppSettingKeys
{
    public const string TrackingEnabled = "tracking.enabled";
    public const string LauncherDiscoveryEnabled = "tracking.launcherDiscoveryEnabled";
    public const string TrackingIntervalSeconds = "tracking.intervalSeconds";
    public const string HeartbeatIntervalSeconds = "tracking.heartbeatIntervalSeconds";
    public const string MinimizeOnClose = "ui.minimizeOnClose";
    public const string StartMinimized = "ui.startMinimized";
    public const string BackupRetentionDays = "backup.retentionDays";
    public const string StartupEnabled = "windows.startupEnabled";
    public const string LastBackupDate = "backup.lastBackupDate";
}
