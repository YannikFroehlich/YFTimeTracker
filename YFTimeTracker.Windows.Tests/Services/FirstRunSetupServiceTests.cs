using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.Services;

[TestClass]
public sealed class FirstRunSetupServiceTests
{
    [TestMethod]
    public async Task LoadOptionsAsync_returns_persisted_choices_and_startup_state()
    {
        var settings = new TestSettingsStore();
        await settings.SetAsync(AppSettingKeys.TrackingEnabled, bool.FalseString, CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.LauncherDiscoveryEnabled, bool.TrueString, CancellationToken.None);
        await settings.SetAsync(AppSettingKeys.MinimizeOnClose, bool.FalseString, CancellationToken.None);
        var startup = new TestStartupService(StartupState.Enabled);
        var service = new FirstRunSetupService(settings, startup);

        var options = await service.LoadOptionsAsync(CancellationToken.None);

        Assert.IsFalse(options.TrackingEnabled);
        Assert.IsTrue(options.LauncherDiscoveryEnabled);
        Assert.IsFalse(options.MinimizeOnClose);
        Assert.IsTrue(options.StartWithWindows);
        Assert.AreEqual(StartupState.Enabled, options.CurrentStartupState);
        Assert.IsTrue(options.CanConfigureStartup);
    }

    [TestMethod]
    public async Task CompleteAsync_persists_choices_and_marks_setup_completed()
    {
        var settings = new TestSettingsStore();
        var startup = new TestStartupService(StartupState.Disabled);
        var service = new FirstRunSetupService(settings, startup);
        var options = new FirstRunSetupOptions(
            TrackingEnabled: true,
            LauncherDiscoveryEnabled: false,
            MinimizeOnClose: true,
            StartWithWindows: true,
            CurrentStartupState: StartupState.Disabled);

        var result = await service.CompleteAsync(options, CancellationToken.None);

        Assert.AreEqual(StartupState.Enabled, result);
        Assert.IsNotNull(startup.LastRequestedState);
        Assert.IsTrue(startup.LastRequestedState.Value);
        Assert.IsTrue(await settings.GetBoolAsync(AppSettingKeys.TrackingEnabled, false, CancellationToken.None));
        Assert.IsFalse(await settings.GetBoolAsync(AppSettingKeys.LauncherDiscoveryEnabled, true, CancellationToken.None));
        Assert.IsTrue(await settings.GetBoolAsync(AppSettingKeys.MinimizeOnClose, false, CancellationToken.None));
        Assert.IsTrue(await service.IsCompletedAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task CompleteAsync_does_not_change_unavailable_startup()
    {
        var settings = new TestSettingsStore();
        var startup = new TestStartupService(StartupState.Unavailable);
        var service = new FirstRunSetupService(settings, startup);
        var options = new FirstRunSetupOptions(true, true, true, true, StartupState.Unavailable);

        var result = await service.CompleteAsync(options, CancellationToken.None);

        Assert.AreEqual(StartupState.Unavailable, result);
        Assert.IsNull(startup.LastRequestedState);
        Assert.IsTrue(await service.IsCompletedAsync(CancellationToken.None));
    }

    private sealed class TestSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(values.GetValueOrDefault(key));
        }

        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values[key] = value;
            return Task.CompletedTask;
        }

        public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken)
        {
            return int.TryParse(await GetAsync(key, cancellationToken), out var value) ? value : fallback;
        }

        public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken)
        {
            return bool.TryParse(await GetAsync(key, cancellationToken), out var value) ? value : fallback;
        }
    }

    private sealed class TestStartupService(StartupState state) : IStartupService
    {
        private StartupState currentState = state;

        public bool? LastRequestedState { get; private set; }

        public Task<StartupState> GetStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(currentState);
        }

        public Task<StartupState> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestedState = enabled;
            currentState = enabled ? StartupState.Enabled : StartupState.Disabled;
            return Task.FromResult(currentState);
        }
    }
}
