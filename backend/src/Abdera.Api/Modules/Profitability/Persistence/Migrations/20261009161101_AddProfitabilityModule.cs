using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Profitability.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfitabilityModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "growth_ideas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    course_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    students = table.Column<int>(type: "integer", nullable: true),
                    teacher_rate_per_lesson = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    discount_months = table.Column<int>(type: "integer", nullable: true),
                    already_coming_percent = table.Column<int>(type: "integer", nullable: true),
                    price_change_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    lost_students = table.Column<int>(type: "integer", nullable: true),
                    monthly_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    one_time_cost = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_growth_ideas", x => x.id);
                    table.CheckConstraint("ck_growth_ideas_one_time_cost", "one_time_cost IS NULL OR one_time_cost >= 0");
                    table.CheckConstraint("ck_growth_ideas_students", "students IS NULL OR students BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "FK_growth_ideas_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "profit_commentaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    refresh_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profit_commentaries", x => x.id);
                    table.CheckConstraint("ck_profit_commentaries_refresh_count", "refresh_count BETWEEN 0 AND 3");
                });

            migrationBuilder.CreateIndex(
                name: "IX_growth_ideas_instrument_id",
                table: "growth_ideas",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "IX_profit_commentaries_period",
                table: "profit_commentaries",
                column: "period",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "growth_ideas");

            migrationBuilder.DropTable(
                name: "profit_commentaries");
        }
    }
}
