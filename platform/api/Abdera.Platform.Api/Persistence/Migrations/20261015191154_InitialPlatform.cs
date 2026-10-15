using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Platform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    ssh_port = table.Column<int>(type: "integer", nullable: false),
                    ssh_user = table.Column<string>(type: "text", nullable: false),
                    host_key_fingerprint = table.Column<string>(type: "text", nullable: false),
                    public_ip = table.Column<string>(type: "text", nullable: false),
                    total_memory_mb = table.Column<int>(type: "integer", nullable: false),
                    reserved_memory_mb = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    agent_version = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hosts", x => x.id);
                    table.CheckConstraint("CK_hosts_reserved", "reserved_memory_mb >= 0");
                });

            migrationBuilder.CreateTable(
                name: "platform_audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    totp_secret_ciphertext = table.Column<string>(type: "text", nullable: true),
                    totp_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_totp_step = table.Column<long>(type: "bigint", nullable: false),
                    security_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_step = table.Column<int>(type: "integer", nullable: false),
                    target_version = table.Column<string>(type: "text", nullable: true),
                    rollout_id = table.Column<Guid>(type: "uuid", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provisioning_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    log = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_steps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "releases",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    api_image = table.Column<string>(type: "text", nullable: false),
                    web_image = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<string>(type: "text", nullable: true),
                    module_catalog_json = table.Column<string>(type: "jsonb", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_withdrawn = table.Column<bool>(type: "boolean", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_releases", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "rollouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    wave = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wave_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    halt_reason = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rollouts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "school_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    level = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "school_secrets",
                columns: table => new
                {
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ciphertext = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_secrets", x => new { x.school_id, x.name });
                });

            migrationBuilder.CreateTable(
                name: "schools",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(31)", maxLength: 31, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    short_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_domain = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    target_version = table.Column<string>(type: "text", nullable: true),
                    modules = table.Column<List<string>>(type: "text[]", nullable: false),
                    primary_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    is_canary = table.Column<bool>(type: "boolean", nullable: false),
                    is_version_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schools", x => x.id);
                    table.CheckConstraint("CK_schools_slug", "slug ~ '^[a-z][a-z0-9-]{2,30}$'");
                });

            migrationBuilder.CreateTable(
                name: "telemetry_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    school_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reachable = table.Column<bool>(type: "boolean", nullable: false),
                    health = table.Column<string>(type: "text", nullable: true),
                    health_detail = table.Column<string>(type: "text", nullable: true),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    active_students = table.Column<int>(type: "integer", nullable: true),
                    active_teachers = table.Column<int>(type: "integer", nullable: true),
                    active_enrollments = table.Column<int>(type: "integer", nullable: true),
                    last_backup_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    database_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    pending_notifications = table.Column<int>(type: "integer", nullable: true),
                    failed_notifications = table.Column<int>(type: "integer", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hosts_name",
                table: "hosts",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_platform_audit_log_created_at",
                table: "platform_audit_log",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_platform_users_email",
                table: "platform_users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_runs_status_created_at",
                table: "provisioning_runs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "UX_provisioning_runs_one_open_per_school",
                table: "provisioning_runs",
                column: "school_id",
                unique: true,
                filter: "status IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_steps_run_id_step_attempt",
                table: "provisioning_steps",
                columns: new[] { "run_id", "step", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_rollouts_one_active",
                table: "rollouts",
                column: "status",
                unique: true,
                filter: "status IN ('Running', 'Paused')");

            migrationBuilder.CreateIndex(
                name: "IX_school_alerts_school_id_received_at",
                table: "school_alerts",
                columns: new[] { "school_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_schools_primary_domain",
                table: "schools",
                column: "primary_domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_schools_slug",
                table: "schools",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_snapshots_school_id_taken_at",
                table: "telemetry_snapshots",
                columns: new[] { "school_id", "taken_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hosts");

            migrationBuilder.DropTable(
                name: "platform_audit_log");

            migrationBuilder.DropTable(
                name: "platform_users");

            migrationBuilder.DropTable(
                name: "provisioning_runs");

            migrationBuilder.DropTable(
                name: "provisioning_steps");

            migrationBuilder.DropTable(
                name: "releases");

            migrationBuilder.DropTable(
                name: "rollouts");

            migrationBuilder.DropTable(
                name: "school_alerts");

            migrationBuilder.DropTable(
                name: "school_secrets");

            migrationBuilder.DropTable(
                name: "schools");

            migrationBuilder.DropTable(
                name: "telemetry_snapshots");
        }
    }
}
