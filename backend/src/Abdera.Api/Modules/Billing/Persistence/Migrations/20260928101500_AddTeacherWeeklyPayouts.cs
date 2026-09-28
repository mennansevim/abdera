using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherWeeklyPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teacher_pay_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_per_lesson = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_pay_rates", x => x.id);
                    table.CheckConstraint("CK_teacher_pay_rates_amount", "amount_per_lesson > 0");
                });

            migrationBuilder.CreateTable(
                name: "teacher_weekly_payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    week_end = table.Column<DateOnly>(type: "date", nullable: false),
                    lesson_count = table.Column<int>(type: "integer", nullable: false),
                    rate_per_lesson = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    computed_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_weekly_payouts", x => x.id);
                    table.CheckConstraint("CK_teacher_weekly_payouts_amount", "amount > 0");
                    table.CheckConstraint("CK_teacher_weekly_payouts_lessons", "lesson_count > 0");
                    table.CheckConstraint("CK_teacher_weekly_payouts_week", "week_end > week_start");
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_pay_rates_teacher",
                table: "teacher_pay_rates",
                column: "teacher_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teacher_weekly_payouts_teacher_week",
                table: "teacher_weekly_payouts",
                columns: new[] { "teacher_id", "week_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teacher_weekly_payouts_week_start",
                table: "teacher_weekly_payouts",
                column: "week_start");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teacher_weekly_payouts");

            migrationBuilder.DropTable(
                name: "teacher_pay_rates");
        }
    }
}
