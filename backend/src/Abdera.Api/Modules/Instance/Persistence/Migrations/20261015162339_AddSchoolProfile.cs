using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Instance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "school_profile",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    short_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    primary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    logo_content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    logo_version = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_profile", x => x.id);
                    table.CheckConstraint("CK_school_profile_logo_consistent", "(logo IS NULL AND logo_content_type IS NULL AND logo_version IS NULL) OR (logo IS NOT NULL AND logo_content_type IS NOT NULL AND logo_version IS NOT NULL)");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "school_profile");
        }
    }
}
