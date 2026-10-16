using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomBranchCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    unit_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_label_plural = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_categories", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branch_categories_key",
                table: "branch_categories",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_branch_categories_name",
                table: "branch_categories",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_categories");
        }
    }
}
