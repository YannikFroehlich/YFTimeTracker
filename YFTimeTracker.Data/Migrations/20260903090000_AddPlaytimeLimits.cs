using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations;

[DbContext(typeof(YFTimeTrackerDbContext))]
[Migration("20260903090000_AddPlaytimeLimits")]
public partial class AddPlaytimeLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "DailyPlaytimeLimitMinutes", table: "Games", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>(name: "WeeklyPlaytimeLimitMinutes", table: "Games", type: "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DailyPlaytimeLimitMinutes", table: "Games");
        migrationBuilder.DropColumn(name: "WeeklyPlaytimeLimitMinutes", table: "Games");
    }
}
