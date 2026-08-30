using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations;

[DbContext(typeof(YFTimeTrackerDbContext))]
[Migration("20260830183000_AddLauncherDiscovery")]
public partial class AddLauncherDiscovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "Source", table: "Games", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "ExternalGameId", table: "Games", type: "TEXT", maxLength: 260, nullable: true);
        migrationBuilder.AddColumn<string>(name: "InstallDirectory", table: "Games", type: "TEXT", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<string>(name: "InstallDirectoryKey", table: "Games", type: "TEXT", maxLength: 1024, nullable: true);

        migrationBuilder.CreateTable(
            name: "GameExecutables",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                GameId = table.Column<long>(type: "INTEGER", nullable: false),
                ExecutablePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                ExecutablePathKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                ExecutableName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                AddedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameExecutables", x => x.Id);
                table.ForeignKey("FK_GameExecutables_Games_GameId", x => x.GameId, "Games", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            INSERT INTO GameExecutables (GameId, ExecutablePath, ExecutablePathKey, ExecutableName, IsPrimary, AddedAtUtc)
            SELECT Id, ExecutablePath, ExecutablePathKey, ExecutableName, 1, AddedAtUtc FROM Games;
            """);

        migrationBuilder.CreateIndex(name: "IX_GameExecutables_ExecutablePathKey", table: "GameExecutables", column: "ExecutablePathKey", unique: true);
        migrationBuilder.CreateIndex(name: "IX_GameExecutables_GameId", table: "GameExecutables", column: "GameId");
        migrationBuilder.CreateIndex(name: "IX_GameExecutables_GameId_Primary", table: "GameExecutables", column: "GameId", unique: true, filter: "IsPrimary = 1");
        migrationBuilder.CreateIndex(name: "IX_Games_Source_ExternalGameId", table: "Games", columns: new[] { "Source", "ExternalGameId" }, unique: true, filter: "ExternalGameId IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "GameExecutables");
        migrationBuilder.DropIndex(name: "IX_Games_Source_ExternalGameId", table: "Games");
        migrationBuilder.DropColumn(name: "Source", table: "Games");
        migrationBuilder.DropColumn(name: "ExternalGameId", table: "Games");
        migrationBuilder.DropColumn(name: "InstallDirectory", table: "Games");
        migrationBuilder.DropColumn(name: "InstallDirectoryKey", table: "Games");
    }
}
