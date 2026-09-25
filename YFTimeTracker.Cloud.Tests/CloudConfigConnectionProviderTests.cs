using Microsoft.Extensions.Logging.Abstractions;

namespace YFTimeTracker.Cloud.Tests;

[TestClass]
public sealed class CloudConfigConnectionProviderTests
{
    private static string Config(string url) =>
        $$"""{ "projectUrl": "{{url}}", "publishableKey": "anon-key" }""";

    [TestMethod]
    public void Https_url_is_accepted()
    {
        var settings = CloudConfigConnectionProvider.Parse(Config(" https://demo.supabase.co/ "), NullLogger.Instance);

        Assert.IsNotNull(settings);
        Assert.AreEqual("https://demo.supabase.co", settings.NormalizedUrl);
    }

    [TestMethod]
    [DataRow("http://demo.supabase.co")]
    [DataRow("demo.supabase.co")]
    [DataRow("ftp://demo.supabase.co")]
    public void Url_without_https_is_rejected_so_passwords_never_travel_unencrypted(string url)
    {
        Assert.IsNull(CloudConfigConnectionProvider.Parse(Config(url), NullLogger.Instance));
    }
}
