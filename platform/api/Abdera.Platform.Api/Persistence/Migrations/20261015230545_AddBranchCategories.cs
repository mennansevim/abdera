using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Platform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "categories",
                table: "schools",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "category_catalog_json",
                table: "releases",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "categories",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "category_catalog_json",
                table: "releases");
        }
    }
}
