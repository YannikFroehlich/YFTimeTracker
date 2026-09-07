using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using YFTimeTracker.Data.Tests.Repositories;

namespace YFTimeTracker.Data.Tests.Initialization;

/// <summary>
/// Guards against the migrations and the <see cref="YFTimeTrackerDbContext"/> model drifting
/// apart: a database built by applying every migration must end up with the same schema as one
/// created straight from the model. Without this, a model change that never got a migration
/// (or a migration that the model no longer describes) stays invisible until someone generates
/// the next migration and it silently contains the wrong operations.
/// </summary>
[TestClass]
public sealed class SchemaConsistencyTests
{
    [TestMethod]
    public async Task Migrations_and_model_produce_the_same_schema()
    {
        var fromMigrations = await ReadSchemaAsync(context => context.Database.MigrateAsync());
        var fromModel = await ReadSchemaAsync(async context => await context.Database.EnsureCreatedAsync());

        Assert.AreEqual(
            fromMigrations,
            fromModel,
            "The migrations and the DbContext model describe different schemas. Either a migration "
                + "is missing for a model change, or the model no longer matches what the migrations build.");
    }

    /// <summary>
    /// Reads tables (with their columns) and named indexes from a freshly built database.
    /// Column order is normalised away because "ALTER TABLE ADD COLUMN" appends columns, and
    /// column defaults are deliberately not compared: SQLite requires a default when adding a
    /// NOT NULL column, so a migrated database legitimately carries defaults the model omits.
    /// </summary>
    private static async Task<string> ReadSchemaAsync(Func<YFTimeTrackerDbContext, Task> createSchemaAsync)
    {
        using var paths = new TempAppPathProvider();
        var factory = new TestDbContextFactory(paths.DatabasePath);
        await using var context = factory.CreateDbContext();

        await createSchemaAsync(context);
        await context.Database.OpenConnectionAsync();

        var connection = context.Database.GetDbConnection();
        var lines = new List<string>();

        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' "
                + "AND name NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        foreach (var table in tables)
        {
            var columns = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\")";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var nullability = reader.GetInt32(3) == 1 ? " NOT NULL" : string.Empty;
                columns.Add($"{reader.GetString(1)} {reader.GetString(2)}{nullability}");
            }

            columns.Sort(StringComparer.Ordinal);
            lines.Add($"TABLE {table}: {string.Join(", ", columns)}");
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT sql FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lines.Add("INDEX " + Regex.Replace(reader.GetString(0), @"\s+", " ").Trim());
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
