using System.Globalization;
using Microsoft.Data.Sqlite;
using YFTimeTracker.Core.Abstractions;
using YFTimeTracker.Core.Models;

namespace YFTimeTracker.Windows.Processes;

/// <summary>
/// Liest die Spielzeit aus der lokalen Datenbank von GOG Galaxy 2.0
/// (<c>%ProgramData%\GOG.com\Galaxy\storage\galaxy-2.0.db</c>, Tabelle <c>GameTimes</c>).
/// Rein lokal, ohne GOG-Anmeldung oder Web-API.
/// </summary>
public sealed class GogPlaytimeReader : ILauncherPlaytimeReader
{
    private readonly string databasePath;

    public GogPlaytimeReader()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GOG.com", "Galaxy", "storage", "galaxy-2.0.db"))
    {
    }

    internal GogPlaytimeReader(string databasePath)
    {
        this.databasePath = databasePath;
    }

    public GameSource Source => GameSource.Gog;

    public async Task<int?> GetPlaytimeMinutesAsync(string productId, CancellationToken cancellationToken)
    {
        if (!File.Exists(databasePath))
        {
            return null;
        }

        try
        {
            await using var connection = LauncherDatabases.OpenReadOnly(databasePath);
            await using var command = connection.CreateCommand();
            // ponytail: kein Filter auf userId. Nutzen mehrere GOG-Konten denselben PC,
            // gilt der höchste Wert. Galaxy markiert das angemeldete Konto nirgends
            // (Users führt nur id); denkbar wäre das zuletzt aktive aus LastPlayedDates.
            command.CommandText = "SELECT MAX(minutesInGame) FROM GameTimes WHERE releaseKey = $releaseKey";
            command.Parameters.AddWithValue("$releaseKey", "gog_" + productId);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull ? null : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        catch (SqliteException exception)
        {
            // Etwa eine von Galaxy gesperrte Datei oder ein geändertes Schema: für die
            // Spieldetails ist das ein Lesefehler wie bei einer unlesbaren Steam-Datei.
            throw new IOException(exception.Message, exception);
        }
    }
}
