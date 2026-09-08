using YFTimeTracker.App.Services;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Tests.Services;

[TestClass]
public sealed class TrayServiceTests
{
    [TestMethod]
    public void Icon_kind_is_paused_when_tracking_paused()
    {
        var state = new TrackingState(true, true, [new RunningGameInfo(1, "Spiel", DateTimeOffset.UtcNow, TimeSpan.Zero)]);

        Assert.AreEqual(TrayIconKind.Paused, TrayService.SelectIconKind(state));
    }

    [TestMethod]
    public void Icon_kind_is_running_when_a_game_is_open()
    {
        var state = new TrackingState(true, false, [new RunningGameInfo(1, "Spiel", DateTimeOffset.UtcNow, TimeSpan.Zero)]);

        Assert.AreEqual(TrayIconKind.Running, TrayService.SelectIconKind(state));
    }

    [TestMethod]
    public void Icon_kind_is_active_when_no_game_is_open()
    {
        var state = new TrackingState(true, false, Array.Empty<RunningGameInfo>());

        Assert.AreEqual(TrayIconKind.Active, TrayService.SelectIconKind(state));
    }

    [TestMethod]
    public void Update_menu_is_actionable_when_idle()
    {
        var state = CreateState(AppUpdateStage.Idle);

        var presentation = TrayService.CreateUpdateMenuPresentation(state);

        Assert.AreEqual("Nach Updates suchen", presentation.Text);
        Assert.IsTrue(presentation.IsEnabled);
    }

    [TestMethod]
    public void Update_menu_shows_available_version()
    {
        var state = CreateState(AppUpdateStage.Available) with { AvailableVersion = "0.5.0" };

        var presentation = TrayService.CreateUpdateMenuPresentation(state);

        Assert.AreEqual("Neue Version 0.5.0 verfügbar", presentation.Text);
        Assert.IsTrue(presentation.IsEnabled);
    }

    [TestMethod]
    public void Update_menu_disables_busy_operation()
    {
        var state = CreateState(AppUpdateStage.Checking);

        var presentation = TrayService.CreateUpdateMenuPresentation(state);

        Assert.AreEqual("Suche nach Updates …", presentation.Text);
        Assert.IsFalse(presentation.IsEnabled);
    }

    [TestMethod]
    public void Update_menu_offers_retry_after_failure()
    {
        var state = CreateState(AppUpdateStage.Failed);

        var presentation = TrayService.CreateUpdateMenuPresentation(state);

        Assert.AreEqual("Update fehlgeschlagen – erneut versuchen", presentation.Text);
        Assert.IsTrue(presentation.IsEnabled);
    }

    private static AppUpdateState CreateState(AppUpdateStage stage)
    {
        return new AppUpdateState(stage, "0.4.0", string.Empty);
    }
}
