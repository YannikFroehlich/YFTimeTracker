using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBaselinePlaytime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaselinePlaytimeMinutes",
                table: "Games",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaselinePlaytimeMinutes",
                table: "Games");
        }
    }
}
