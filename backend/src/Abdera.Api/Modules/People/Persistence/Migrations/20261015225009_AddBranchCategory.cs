using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "instruments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "music");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "instruments",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                table: "instruments");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "instruments");
        }
    }
}
