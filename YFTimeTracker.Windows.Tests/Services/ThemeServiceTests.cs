using Microsoft.UI.Xaml;
using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.Services;

[TestClass]
public sealed class ThemeServiceTests
{
    [TestMethod]
    public async Task Initialize_defaults_to_dark_when_nothing_is_stored()
    {
        var service = new ThemeService(new InMemorySettingsStore());

        await service.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(AppThemePreference.Dark, service.CurrentPreference);
        Assert.AreEqual(ElementTheme.Dark, service.CurrentTheme);
    }

    [TestMethod]
    public async Task SetTheme_persists_and_resolves_system_to_default()
    {
        var store = new InMemorySettingsStore();
        var service = new ThemeService(store);
        await service.InitializeAsync(CancellationToken.None);

        await service.SetThemeAsync(AppThemePreference.System, CancellationToken.None);

        Assert.AreEqual(AppThemePreference.System, service.CurrentPreference);
        Assert.AreEqual(ElementTheme.Default, service.CurrentTheme);
        Assert.AreEqual("System", await store.GetAsync(AppSettingKeys.Theme, CancellationToken.None));
    }

    [TestMethod]
    public async Task SetTheme_raises_ThemeChanged_with_resolved_value()
    {
        var service = new ThemeService(new InMemorySettingsStore());
        await service.InitializeAsync(CancellationToken.None);
        ElementTheme? raised = null;
        service.ThemeChanged += (_, theme) => raised = theme;

        await service.SetThemeAsync(AppThemePreference.Light, CancellationToken.None);

        Assert.AreEqual(ElementTheme.Light, raised);
    }

    [TestMethod]
    public async Task Initialize_round_trips_a_previously_stored_preference()
    {
        var store = new InMemorySettingsStore();
        await store.SetAsync(AppSettingKeys.Theme, "Light", CancellationToken.None);
        var service = new ThemeService(store);

        await service.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(AppThemePreference.Light, service.CurrentPreference);
        Assert.AreEqual(ElementTheme.Light, service.CurrentTheme);
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> values = [];

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            values[key] = value;
            return Task.CompletedTask;
        }

        public Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback);

        public Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback);
    }
}
