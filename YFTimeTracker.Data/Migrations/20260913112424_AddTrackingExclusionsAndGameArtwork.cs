using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YFTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingExclusionsAndGameArtwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameArtworks",
                columns: table => new
                {
                    GameId = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ImageData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameArtworks", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_GameArtworks_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackingExclusionRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ValueKey = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AddedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingExclusionRules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackingExclusionRules_Kind_ValueKey",
                table: "TrackingExclusionRules",
                columns: new[] { "Kind", "ValueKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameArtworks");

            migrationBuilder.DropTable(
                name: "TrackingExclusionRules");
        }
    }
}
