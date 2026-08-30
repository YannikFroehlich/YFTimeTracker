using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations;

[DbContext(typeof(YFTimeTrackerDbContext))]
[Migration("20260830012000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AppSettings",
            columns: table => new
            {
                Key = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Value = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppSettings", x => x.Key);
            });

        migrationBuilder.CreateTable(
            name: "Games",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                ExecutablePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                ExecutablePathKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                ExecutableName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                AddedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Games", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "GameSessions",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                GameId = table.Column<long>(type: "INTEGER", nullable: false),
                StartedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                LastSeenAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                EndedAtUtc = table.Column<long>(type: "INTEGER", nullable: true),
                DurationSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                BootSessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GameSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_GameSessions_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_GameSessions_GameId",
            table: "GameSessions",
            column: "GameId");

        migrationBuilder.CreateIndex(
            name: "IX_GameSessions_GameId_Open",
            table: "GameSessions",
            column: "GameId",
            unique: true,
            filter: "EndedAtUtc IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Games_ExecutablePathKey",
            table: "Games",
            column: "ExecutablePathKey",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AppSettings");
        migrationBuilder.DropTable(name: "GameSessions");
        migrationBuilder.DropTable(name: "Games");
    }
}
