using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class SteamPlaytimeReaderTests
{
    [TestMethod]
    public async Task Playtime_comes_from_most_recent_login_not_newest_localconfig()
    {
        var steamRoot = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-Steam-{Guid.NewGuid():N}");
        try
        {
            WriteLocalConfig(steamRoot, "12345", playtime: 100, DateTime.UtcNow.AddDays(-1));
            WriteLocalConfig(steamRoot, "67890", playtime: 999, DateTime.UtcNow);
            var loginUsers = Path.Combine(steamRoot, "config", "loginusers.vdf");
            Directory.CreateDirectory(Path.GetDirectoryName(loginUsers)!);
            File.WriteAllText(loginUsers, """
                "users"
                {
                    "76561197960333618" { "AccountName" "zweiter" "MostRecent" "0" }
                    "76561197960278073" { "AccountName" "erster" "MostRecent" "1" }
                }
                """);
            var reader = new SteamPlaytimeReader(() => steamRoot);

            Assert.AreEqual(100, await reader.GetPlaytimeMinutesAsync("440", CancellationToken.None));

            File.Delete(loginUsers);
            Assert.AreEqual(999, await reader.GetPlaytimeMinutesAsync("440", CancellationToken.None),
                "Ohne loginusers.vdf gilt die zuletzt geschriebene localconfig.vdf.");
        }
        finally
        {
            Directory.Delete(steamRoot, true);
        }
    }

    private static void WriteLocalConfig(string steamRoot, string accountId, int playtime, DateTime lastWriteUtc)
    {
        var file = Path.Combine(steamRoot, "userdata", accountId, "config", "localconfig.vdf");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $$"""
            "UserLocalConfigStore"
            {
                "Software" { "Valve" { "Steam" { "apps" { "440" { "Playtime" "{{playtime}}" } } } } }
            }
            """);
        File.SetLastWriteTimeUtc(file, lastWriteUtc);
    }
}
