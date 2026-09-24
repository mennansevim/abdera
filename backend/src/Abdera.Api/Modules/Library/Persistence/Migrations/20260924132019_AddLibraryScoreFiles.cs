using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Library.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryScoreFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library_score_files",
                columns: table => new
                {
                    entry_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_score_files", x => x.entry_id);
                    table.CheckConstraint("ck_library_score_files_pages", "page_count > 0");
                    table.CheckConstraint("ck_library_score_files_size", "size_bytes > 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "library_score_files");
        }
    }
}
