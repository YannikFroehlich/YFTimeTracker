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
    public const string BackupDestination = "backup.destination";
    public const string BackupExternalFolderPath = "backup.externalFolderPath";
    public const string StartupEnabled = "windows.startupEnabled";
    public const string LastBackupDate = "backup.lastBackupDate";
    public const string Theme = "ui.theme";
    public const string LastSeenChangelogHeading = "ui.lastSeenChangelogHeading";
    public const string ProfileDisplayName = "profile.displayName";
    public const string ProfileAccentColor = "profile.accentColor";
    public const string GlobalSearchRecentQueries = "ui.globalSearchRecentQueries";
    public const string LastLoggedUpdateVersion = "updates.lastLoggedVersion";
    public const string UpdateRemindVersion = "updates.remindVersion";
    public const string UpdateRemindAfterUtc = "updates.remindAfterUtc";

    // Kontoabgleich (Supabase). Zugangsdaten stehen bewusst nicht hier, sondern
    // im Windows-Anmeldeinformationsspeicher; in der Datenbank landen nur
    // unkritische Angaben wie die zuletzt angemeldete E-Mail und Zeitstempel.
    public const string CloudUserEmail = "cloud.userEmail";
    public const string CloudDeviceId = "cloud.deviceId";
    public const string CloudLastSyncUtc = "cloud.lastSyncUtc";
    public const string CloudSyncArtwork = "cloud.syncArtwork";

    // Geraetenamen aus dem Konto, damit Sessions eines zweiten PCs auch ohne
    // Netz benennbar bleiben. Bewusst geraetegebunden: der Inhalt ist eine
    // Zwischenablage des Kontostands, kein abzugleichender Datensatz.
    public const string CloudKnownDevices = "cloud.knownDevices";
}
