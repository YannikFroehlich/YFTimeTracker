using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSyncState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudId",
                table: "TrackingExclusionRules",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedHash",
                table: "TrackingExclusionRules",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudId",
                table: "GameTags",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedHash",
                table: "GameTags",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudId",
                table: "GameSessions",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedHash",
                table: "GameSessions",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudId",
                table: "Games",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedHash",
                table: "Games",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudId",
                table: "GameExecutables",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedHash",
                table: "GameExecutables",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudId",
                table: "GameArtworks",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedHash",
                table: "GameArtworks",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncedHash",
                table: "AppSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SyncTombstones",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Identity = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    CloudId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    DeletedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncTombstones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncTombstones_Kind_Identity",
                table: "SyncTombstones",
                columns: new[] { "Kind", "Identity" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncTombstones");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "TrackingExclusionRules");

            migrationBuilder.DropColumn(
                name: "SyncedHash",
                table: "TrackingExclusionRules");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "GameTags");

            migrationBuilder.DropColumn(
                name: "SyncedHash",
                table: "GameTags");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "SyncedHash",
                table: "GameSessions");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "SyncedHash",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "GameExecutables");

            migrationBuilder.DropColumn(
                name: "SyncedHash",
                table: "GameExecutables");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "GameArtworks");

            migrationBuilder.DropColumn(
                name: "SyncedHash",
                table: "GameArtworks");

            migrationBuilder.DropColumn(
                name: "SyncedHash",
                table: "AppSettings");
        }
    }
}
