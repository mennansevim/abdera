using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryPiecesAndSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_library_score_files_pages",
                table: "library_score_files");

            migrationBuilder.AlterColumn<int>(
                name: "page_count",
                table: "library_score_files",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "library_pieces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    composer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instrument = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_pieces", x => x.id);
                    table.CheckConstraint("ck_library_pieces_level", "level IS NULL OR level BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "library_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    composer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_suggestions", x => x.id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_library_score_files_pages",
                table: "library_score_files",
                sql: "page_count IS NULL OR page_count > 0");

            migrationBuilder.CreateIndex(
                name: "IX_library_suggestions_student_id_entry_id",
                table: "library_suggestions",
                columns: new[] { "student_id", "entry_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_pieces");

            migrationBuilder.DropTable(
                name: "library_suggestions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_library_score_files_pages",
                table: "library_score_files");

            // Geri alınabilir olsun: eklenen eserlerin PDF'leri tablosuyla birlikte gider, tarayıcıdan
            // yüklenmiş (sayfa sayısı bilinmeyen) dosyalar NOT NULL + CHECK'i geçsin diye 1 sayılır.
            migrationBuilder.Sql("DELETE FROM library_score_files WHERE entry_id LIKE 'piece-%';");
            migrationBuilder.Sql("UPDATE library_score_files SET page_count = 1 WHERE page_count IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "page_count",
                table: "library_score_files",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_library_score_files_pages",
                table: "library_score_files",
                sql: "page_count > 0");
        }
    }
}
