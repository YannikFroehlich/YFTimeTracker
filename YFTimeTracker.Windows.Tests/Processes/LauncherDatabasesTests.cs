using Microsoft.Data.Sqlite;
using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class LauncherDatabasesTests
{
    [TestMethod]
    public void Amazon_games_are_the_installed_rows_of_DbSet()
    {
        WithDatabase("""
            CREATE TABLE DbSet (Id TEXT, ProductTitle TEXT, InstallDirectory TEXT, Installed INTEGER, ProductAsin TEXT);
            INSERT INTO DbSet VALUES
              ('amzn1.adg.product.1', 'Neon', 'C:\Amazon Games\Library\Neon', 1, 'B0001'),
              ('amzn1.adg.product.2', 'Deinstalliert', 'C:\Amazon Games\Library\Gone', 0, 'B0002'),
              ('amzn1.adg.product.3', 'Ohne Ordner', NULL, 1, 'B0003');
            """, database =>
        {
            var games = LauncherDatabases.ReadAmazonGames(database);

            Assert.HasCount(1, games);
            Assert.AreEqual(new LauncherDatabases.AmazonGame("amzn1.adg.product.1", "Neon", @"C:\Amazon Games\Library\Neon"), games[0]);
        });
    }

    [TestMethod]
    public void Itch_caves_use_custom_folder_or_install_location()
    {
        WithDatabase("""
            CREATE TABLE games (id INTEGER PRIMARY KEY, title TEXT);
            CREATE TABLE install_locations (id TEXT PRIMARY KEY, path TEXT);
            CREATE TABLE caves (id TEXT PRIMARY KEY, game_id INTEGER, install_location_id TEXT,
              install_folder_name TEXT, custom_install_folder TEXT, verdict TEXT);
            INSERT INTO games VALUES (100, 'Neon'), (200, 'Eigener Ordner'), (300, 'Unvollständig');
            INSERT INTO install_locations VALUES ('loc', 'C:\itch\apps');
            INSERT INTO caves VALUES
              ('a', 100, 'loc', 'neon', '', '{"candidates":[]}'),
              ('b', 200, '', '', 'D:\Spiele\Eigen', NULL),
              ('c', 300, 'fehlt', 'weg', '', NULL);
            """, database =>
        {
            var caves = LauncherDatabases.ReadItchCaves(database).OrderBy(cave => cave.GameId).ToArray();

            Assert.HasCount(2, caves);
            Assert.AreEqual(new LauncherDatabases.ItchCave("100", "Neon", @"C:\itch\apps\neon", """{"candidates":[]}"""), caves[0]);
            Assert.AreEqual(new LauncherDatabases.ItchCave("200", "Eigener Ordner", @"D:\Spiele\Eigen", null), caves[1]);
        });
    }

    private static void WithDatabase(string sql, Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-LauncherDb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "launcher.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }

            test(database);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
