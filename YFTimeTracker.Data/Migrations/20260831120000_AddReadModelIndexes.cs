using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations;

[DbContext(typeof(YFTimeTrackerDbContext))]
[Migration("20260831120000_AddReadModelIndexes")]
public partial class AddReadModelIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Games_Name",
            table: "Games",
            column: "Name");
        migrationBuilder.CreateIndex(
            name: "IX_GameSessions_StartedAtUtc",
            table: "GameSessions",
            column: "StartedAtUtc");
        migrationBuilder.CreateIndex(
            name: "IX_GameSessions_EndedAtUtc",
            table: "GameSessions",
            column: "EndedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Games_Name", table: "Games");
        migrationBuilder.DropIndex(name: "IX_GameSessions_StartedAtUtc", table: "GameSessions");
        migrationBuilder.DropIndex(name: "IX_GameSessions_EndedAtUtc", table: "GameSessions");
    }
}
