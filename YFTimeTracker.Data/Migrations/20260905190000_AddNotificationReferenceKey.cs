using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations;

[DbContext(typeof(YFTimeTrackerDbContext))]
[Migration("20260905190000_AddNotificationReferenceKey")]
public partial class AddNotificationReferenceKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReferenceKey",
            table: "NotificationLogEntries",
            type: "TEXT",
            maxLength: 160,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_NotificationLogEntries_Kind_ReferenceKey",
            table: "NotificationLogEntries",
            columns: ["Kind", "ReferenceKey"],
            filter: "ReferenceKey IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_NotificationLogEntries_Kind_ReferenceKey",
            table: "NotificationLogEntries");

        migrationBuilder.DropColumn(name: "ReferenceKey", table: "NotificationLogEntries");
    }
}
