using YFTimeTracker.App.Services;

namespace YFTimeTracker.Windows.Tests.Services;

[TestClass]
public sealed class TrayServiceTests
{
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

    private static AppUpdateState CreateState(AppUpdateStage stage)
    {
        return new AppUpdateState(stage, "0.4.0", string.Empty);
    }
}
