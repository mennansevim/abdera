using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Messaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "message_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false, defaultValue: "tr"),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    recipient_phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    body_snapshot = table.Column<string>(type: "text", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    processing_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_webhook_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_message_templates_name",
                table: "message_templates",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_jobs_status_scheduled_at",
                table: "notification_jobs",
                columns: new[] { "status", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_jobs_type_reference_type_reference_id",
                table: "notification_jobs",
                columns: new[] { "type", "reference_type", "reference_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_messages_guardian_id",
                table: "whatsapp_messages",
                column: "guardian_id");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_messages_notification_job_id",
                table: "whatsapp_messages",
                column: "notification_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_webhook_events_provider_event_id",
                table: "whatsapp_webhook_events",
                column: "provider_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_templates");

            migrationBuilder.DropTable(
                name: "notification_jobs");

            migrationBuilder.DropTable(
                name: "whatsapp_messages");

            migrationBuilder.DropTable(
                name: "whatsapp_webhook_events");
        }
    }
}
