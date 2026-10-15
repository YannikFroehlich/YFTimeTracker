using Microsoft.Data.Sqlite;
using YFTimeTracker.Windows.Processes;

namespace YFTimeTracker.Windows.Tests.Processes;

[TestClass]
public sealed class GogPlaytimeReaderTests
{
    [TestMethod]
    public async Task Playtime_is_read_by_gog_release_key()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"YFTimeTracker-Gog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "galaxy-2.0.db");
        try
        {
            var reader = new GogPlaytimeReader(database);
            Assert.IsNull(await reader.GetPlaytimeMinutesAsync("1207658924", CancellationToken.None), "Ohne Galaxy keine Spielzeit.");

            await ExecuteAsync(database, "CREATE TABLE Other (id INTEGER)");
            await Assert.ThrowsExactlyAsync<IOException>(() => reader.GetPlaytimeMinutesAsync("1207658924", CancellationToken.None));

            await ExecuteAsync(database, """
                CREATE TABLE GameTimes (userId INTEGER, releaseKey TEXT, minutesInGame INTEGER);
                INSERT INTO GameTimes VALUES (1, 'gog_1207658924', 754), (1, 'steam_1207658924', 999), (1, 'gog_1', 5);
                """);

            Assert.AreEqual(754, await reader.GetPlaytimeMinutesAsync("1207658924", CancellationToken.None));
            Assert.IsNull(await reader.GetPlaytimeMinutesAsync("42", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
