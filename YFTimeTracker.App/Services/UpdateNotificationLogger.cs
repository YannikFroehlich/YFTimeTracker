using Microsoft.Extensions.Logging;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.Services;

public sealed class UpdateNotificationLogger(
    IAppUpdateService updateService,
    INotificationLogRepository notificationLog,
    ISettingsStore settings,
    IClock clock,
    ILogger<UpdateNotificationLogger> logger) : IUpdateNotificationLogger
{
    private bool initialized;

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        updateService.StateChanged += UpdateService_StateChanged;
    }

    public void Dispose()
    {
        updateService.StateChanged -= UpdateService_StateChanged;
    }

    private async void UpdateService_StateChanged(object? sender, AppUpdateState state)
    {
        if (!state.HasAvailableUpdate || state.AvailableVersion is not { } version)
        {
            return;
        }

        try
        {
            var lastLoggedVersion = await settings.GetAsync(AppSettingKeys.LastLoggedUpdateVersion, CancellationToken.None);
            if (string.Equals(lastLoggedVersion, version, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await settings.SetAsync(AppSettingKeys.LastLoggedUpdateVersion, version, CancellationToken.None);
            await notificationLog.AddAsync(new NotificationLogEntry
            {
                Kind = NotificationKind.UpdateAvailable,
                Title = "Update verfügbar",
                Message = $"YFTimeTracker {version} steht bereit.",
                CreatedAtUtc = clock.UtcNow
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to write update notification log entry for version {Version}.", version);
        }
    }
}
