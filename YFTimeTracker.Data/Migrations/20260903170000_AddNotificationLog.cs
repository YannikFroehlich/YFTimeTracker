using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations;

[DbContext(typeof(YFTimeTrackerDbContext))]
[Migration("20260903170000_AddNotificationLog")]
public partial class AddNotificationLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NotificationLogEntries",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                RelatedGameId = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationLogEntries", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NotificationLogEntries_CreatedAtUtc",
            table: "NotificationLogEntries",
            column: "CreatedAtUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "NotificationLogEntries");
    }
}
