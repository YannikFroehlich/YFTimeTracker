using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionIsManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "GameSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "GameSessions");
        }
    }
}
