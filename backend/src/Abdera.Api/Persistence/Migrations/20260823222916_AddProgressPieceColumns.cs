using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressPieceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "piece_difficulty",
                table: "lesson_notes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "piece_title",
                table: "lesson_notes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "piece_difficulty",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "piece_title",
                table: "lesson_notes");
        }
    }
}
