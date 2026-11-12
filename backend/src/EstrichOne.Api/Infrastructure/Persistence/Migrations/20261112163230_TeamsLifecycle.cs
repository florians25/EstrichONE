using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamsLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "chats_via_graph",
                table: "teams_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "close_action",
                table: "teams_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Archive");

            migrationBuilder.AddColumn<bool>(
                name: "rollout_notice",
                table: "teams_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "rollout_notice_text",
                table: "teams_settings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "teams_bauakte_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "close_error",
                table: "teams_bauakte_links",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "close_handled_at",
                table: "teams_bauakte_links",
                type: "timestamp with time zone",
                nullable: true);

            // Bestehende Verknüpfungen bereits abgeschlossener Bauakten nicht nachträglich archivieren (gilt erst ab jetzt).
            migrationBuilder.Sql("UPDATE teams_bauakte_links SET close_handled_at = now();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "chats_via_graph",
                table: "teams_settings");

            migrationBuilder.DropColumn(
                name: "close_action",
                table: "teams_settings");

            migrationBuilder.DropColumn(
                name: "rollout_notice",
                table: "teams_settings");

            migrationBuilder.DropColumn(
                name: "rollout_notice_text",
                table: "teams_settings");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "teams_bauakte_links");

            migrationBuilder.DropColumn(
                name: "close_error",
                table: "teams_bauakte_links");

            migrationBuilder.DropColumn(
                name: "close_handled_at",
                table: "teams_bauakte_links");
        }
    }
}
