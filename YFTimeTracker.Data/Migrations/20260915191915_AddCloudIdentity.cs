using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudIdentity",
                table: "TrackingExclusionRules",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudIdentity",
                table: "GameTags",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudIdentity",
                table: "GameSessions",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudIdentity",
                table: "Games",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudIdentity",
                table: "GameExecutables",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudIdentity",
                table: "GameArtworks",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloudIdentity",
                table: "TrackingExclusionRules");

            migrationBuilder.DropColumn(
                name: "CloudIdentity",
                table: "GameTags");

            migrationBuilder.DropColumn(
                name: "CloudIdentity",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "CloudIdentity",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "CloudIdentity",
                table: "GameExecutables");

            migrationBuilder.DropColumn(
                name: "CloudIdentity",
                table: "GameArtworks");
        }
    }
}
