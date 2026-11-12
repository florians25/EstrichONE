using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamsIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teams_bauakte_links",
                columns: table => new
                {
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created = table.Column<bool>(type: "boolean", nullable: false),
                    team_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    channel_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    chat_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    web_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sync_members = table.Column<bool>(type: "boolean", nullable: false),
                    managed_member_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    protected_webhook_url = table.Column<string>(type: "text", nullable: true),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    synced_members = table.Column<int>(type: "integer", nullable: false),
                    unmapped_members = table.Column<int>(type: "integer", nullable: false),
                    last_post_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_post_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sync_pending = table.Column<bool>(type: "boolean", nullable: false),
                    desired_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams_bauakte_links", x => x.bauakte_id);
                });

            migrationBuilder.CreateTable(
                name: "teams_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    chat_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    auth_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    protected_client_secret = table.Column<string>(type: "text", nullable: true),
                    protected_certificate = table.Column<string>(type: "text", nullable: true),
                    certificate_thumbprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    certificate_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    site_team_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    site_team_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    site_team_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    default_link_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    auto_link = table.Column<bool>(type: "boolean", nullable: false),
                    auto_link_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sync_members = table.Column<bool>(type: "boolean", nullable: false),
                    post_via = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    protected_webhook_url = table.Column<string>(type: "text", nullable: true),
                    post_defects = table.Column<bool>(type: "boolean", nullable: false),
                    post_acceptance = table.Column<bool>(type: "boolean", nullable: false),
                    post_schedule = table.Column<bool>(type: "boolean", nullable: false),
                    protected_sender_token = table.Column<string>(type: "text", nullable: true),
                    sender_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sender_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sender_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    protected_device_code = table.Column<string>(type: "text", nullable: true),
                    device_code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    last_post_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_post_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams_user_links",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aad_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_principal_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manual = table.Column<bool>(type: "boolean", nullable: false),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams_user_links", x => x.user_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teams_bauakte_links");

            migrationBuilder.DropTable(
                name: "teams_settings");

            migrationBuilder.DropTable(
                name: "teams_user_links");
        }
    }
}
