using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Instance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "school_policy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_lesson_share_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    max_weekly_lessons = table.Column<int>(type: "integer", nullable: false),
                    makeup_notice_hours = table.Column<int>(type: "integer", nullable: false),
                    makeup_credit_valid_days = table.Column<int>(type: "integer", nullable: false),
                    excused_makeup_window_days = table.Column<int>(type: "integer", nullable: false),
                    school_day_start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    school_day_end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_policy", x => x.id);
                    table.CheckConstraint("CK_school_policy_day", "school_day_end > school_day_start");
                    table.CheckConstraint("CK_school_policy_positive", "max_weekly_lessons > 0 AND makeup_notice_hours >= 0 AND makeup_credit_valid_days > 0 AND excused_makeup_window_days > 0");
                    table.CheckConstraint("CK_school_policy_share", "group_lesson_share_percent >= 0 AND group_lesson_share_percent <= 100");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "school_policy");
        }
    }
}
