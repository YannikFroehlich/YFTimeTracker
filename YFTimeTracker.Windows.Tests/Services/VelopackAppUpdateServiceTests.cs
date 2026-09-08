using YFTimeTracker.App.Services;

namespace YFTimeTracker.Windows.Tests.Services;

[TestClass]
public sealed class VelopackAppUpdateServiceTests
{
    [TestMethod]
    public void Describes_network_failures_as_a_connectivity_problem()
    {
        var message = VelopackAppUpdateService.DescribeFailure(new HttpRequestException("boom"), "Update-Prüfung fehlgeschlagen.");

        StringAssert.Contains(message, "Internetverbindung");
    }

    [TestMethod]
    public void Describes_locked_file_failures_as_an_access_problem()
    {
        var message = VelopackAppUpdateService.DescribeFailure(new IOException("boom"), "Update konnte nicht heruntergeladen werden.");

        StringAssert.Contains(message, "Antivirus");
    }

    [TestMethod]
    public void Falls_back_to_a_generic_retry_hint_for_unknown_failures()
    {
        var message = VelopackAppUpdateService.DescribeFailure(new InvalidOperationException("boom"), "Update konnte nicht heruntergeladen werden.");

        Assert.AreEqual("Update konnte nicht heruntergeladen werden. Bitte später erneut versuchen.", message);
    }
}
