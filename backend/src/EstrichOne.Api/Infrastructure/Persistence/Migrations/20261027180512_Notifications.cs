using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_broadcasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sender_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    urgency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    audience = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_broadcasts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    urgency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    link = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    object_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    object_id = table.Column<Guid>(type: "uuid", nullable: true),
                    broadcast_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sender_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dedup_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    push_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pushed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    push_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    token = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    auth_secret = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    bundle_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    device_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    web_push_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    vapid_public_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    protected_vapid_private_key = table.Column<string>(type: "text", nullable: true),
                    vapid_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    apns_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    apns_key_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    apns_team_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    apns_bundle_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    apns_production = table.Column<bool>(type: "boolean", nullable: false),
                    protected_apns_key = table.Column<string>(type: "text", nullable: true),
                    show_content = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_broadcasts_created_at",
                table: "notification_broadcasts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_broadcast_id",
                table: "notifications",
                column: "broadcast_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_push_state",
                table: "notifications",
                column: "push_state",
                filter: "push_state = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at",
                table: "notifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_dedup_key",
                table: "notifications",
                columns: new[] { "user_id", "dedup_key" },
                unique: true,
                filter: "dedup_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_push_devices_token_hash",
                table: "push_devices",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_push_devices_user_id",
                table: "push_devices",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_broadcasts");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "push_devices");

            migrationBuilder.DropTable(
                name: "push_settings");
        }
    }
}
