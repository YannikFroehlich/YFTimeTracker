using Microsoft.Data.Sqlite;

namespace YFTimeTracker.Windows.Processes;

/// <summary>
/// Liest die SQLite-Datenbanken, in denen Launcher ihre Installationen führen.
/// </summary>
internal static class LauncherDatabases
{
    internal sealed record AmazonGame(string Id, string Name, string InstallDirectory);

    internal sealed record ItchCave(string GameId, string Name, string InstallDirectory, string? Verdict);

    /// <summary>
    /// Öffnet eine Launcher-Datenbank nur lesend. Ohne Pooling bliebe sie nach dem Lesen
    /// weiter geöffnet, obwohl der Launcher selbst hineinschreibt.
    /// </summary>
    public static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Amazon Games: <c>GameInstallInfo.sqlite</c>, Tabelle <c>DbSet</c>.
    /// </summary>
    public static IReadOnlyList<AmazonGame> ReadAmazonGames(string databasePath)
    {
        using var connection = OpenReadOnly(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProductTitle, InstallDirectory FROM DbSet WHERE Installed = 1";
        using var reader = command.ExecuteReader();
        var games = new List<AmazonGame>();
        while (reader.Read())
        {
            if (GetText(reader, 0) is { } id && GetText(reader, 1) is { } name && GetText(reader, 2) is { } directory)
            {
                games.Add(new AmazonGame(id, name, directory));
            }
        }

        return games;
    }

    /// <summary>
    /// itch.io: <c>butler.db</c> der itch-App. Eine Cave ist eine Installation; ihr Ordner ist
    /// entweder frei gewählt oder liegt als <c>install_folder_name</c> unter einem Installationsort.
    /// </summary>
    public static IReadOnlyList<ItchCave> ReadItchCaves(string databasePath)
    {
        using var connection = OpenReadOnly(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.game_id, g.title, c.custom_install_folder, l.path, c.install_folder_name, c.verdict
            FROM caves c
            JOIN games g ON g.id = c.game_id
            LEFT JOIN install_locations l ON l.id = c.install_location_id
            """;
        using var reader = command.ExecuteReader();
        var caves = new List<ItchCave>();
        while (reader.Read())
        {
            var directory = GetText(reader, 2)
                ?? (GetText(reader, 3) is { } location && GetText(reader, 4) is { } folder ? Path.Combine(location, folder) : null);
            if (GetText(reader, 0) is { } gameId && GetText(reader, 1) is { } name && directory is not null)
            {
                caves.Add(new ItchCave(gameId, name, directory, GetText(reader, 5)));
            }
        }

        return caves;
    }

    private static string? GetText(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) || reader.GetString(ordinal) is not { } value || string.IsNullOrWhiteSpace(value)
            ? null
            : value;
}
