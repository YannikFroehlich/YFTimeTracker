using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.App.Services;

public sealed record FirstRunSetupOptions(
    bool TrackingEnabled,
    bool LauncherDiscoveryEnabled,
    bool MinimizeOnClose,
    bool StartWithWindows,
    StartupState CurrentStartupState)
{
    public bool CanConfigureStartup => CurrentStartupState is StartupState.Disabled or StartupState.Enabled;
}

public interface IFirstRunSetupService
{
    Task<bool> IsCompletedAsync(CancellationToken cancellationToken);

    Task<FirstRunSetupOptions> LoadOptionsAsync(CancellationToken cancellationToken);

    Task<StartupState> CompleteAsync(FirstRunSetupOptions options, CancellationToken cancellationToken);
}

public sealed class FirstRunSetupService(
    ISettingsStore settings,
    IStartupService startupService) : IFirstRunSetupService
{
    public Task<bool> IsCompletedAsync(CancellationToken cancellationToken)
    {
        return settings.GetBoolAsync(AppSettingKeys.FirstRunSetupCompleted, false, cancellationToken);
    }

    public async Task<FirstRunSetupOptions> LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var startupState = await startupService.GetStateAsync(cancellationToken);
        return new FirstRunSetupOptions(
            await settings.GetBoolAsync(AppSettingKeys.TrackingEnabled, true, cancellationToken),
            await settings.GetBoolAsync(AppSettingKeys.LauncherDiscoveryEnabled, true, cancellationToken),
            await settings.GetBoolAsync(AppSettingKeys.MinimizeOnClose, true, cancellationToken),
            startupState == StartupState.Enabled,
            startupState);
    }

    public async Task<StartupState> CompleteAsync(
        FirstRunSetupOptions options,
        CancellationToken cancellationToken)
    {
        await settings.SetAsync(AppSettingKeys.TrackingEnabled, options.TrackingEnabled.ToString(), cancellationToken);
        await settings.SetAsync(
            AppSettingKeys.LauncherDiscoveryEnabled,
            options.LauncherDiscoveryEnabled.ToString(),
            cancellationToken);
        await settings.SetAsync(AppSettingKeys.MinimizeOnClose, options.MinimizeOnClose.ToString(), cancellationToken);

        var currentStartupState = await startupService.GetStateAsync(cancellationToken);
        var startupState = currentStartupState is StartupState.Unavailable or StartupState.DisabledByPolicy
            ? currentStartupState
            : await startupService.SetEnabledAsync(options.StartWithWindows, cancellationToken);

        await settings.SetAsync(AppSettingKeys.FirstRunSetupCompleted, bool.TrueString, cancellationToken);
        return startupState;
    }
}
