using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Platform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomCategoryNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_category_names_json",
                table: "schools",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "custom_category_names_json",
                table: "schools");
        }
    }
}
