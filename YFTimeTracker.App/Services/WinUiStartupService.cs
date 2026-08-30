using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.ApplicationModel;
using YFTimeTracker.Core.Abstractions;

namespace YFTimeTracker.App.Services;

public sealed class WinUiStartupService(ILogger<WinUiStartupService> logger) : IStartupService
{
    private const string StartupTaskId = "YFTimeTrackerStartupTask";
    private const string RegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "YFTimeTracker";

    public async Task<StartupState> GetStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsPackaged())
        {
            return GetUnpackagedState();
        }

        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            return MapState(task.State);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Windows startup task is not available in the current package context.");
            return StartupState.Unavailable;
        }
    }

    public async Task<StartupState> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsPackaged())
        {
            return SetUnpackagedState(enabled);
        }

        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                return MapState(await task.RequestEnableAsync());
            }

            if (task.State == StartupTaskState.Enabled)
            {
                task.Disable();
            }

            return MapState(task.State);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Windows startup task could not be changed in the current package context.");
            return StartupState.Unavailable;
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current.Id.Name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static StartupState GetUnpackagedState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, writable: false);
        return key?.GetValue(RegistryValueName) is string value && !string.IsNullOrWhiteSpace(value)
            ? StartupState.Enabled
            : StartupState.Disabled;
    }

    private static StartupState SetUnpackagedState(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true);
        if (!enabled)
        {
            key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
            return StartupState.Disabled;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return StartupState.Unavailable;
        }

        key.SetValue(RegistryValueName, $"\"{executablePath}\" --minimized", RegistryValueKind.String);
        return StartupState.Enabled;
    }

    private static StartupState MapState(StartupTaskState state)
    {
        return state switch
        {
            StartupTaskState.Enabled => StartupState.Enabled,
            StartupTaskState.EnabledByPolicy => StartupState.Enabled,
            StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
            StartupTaskState.DisabledByUser => StartupState.Disabled,
            StartupTaskState.Disabled => StartupState.Disabled,
            _ => StartupState.Unavailable
        };
    }
}
