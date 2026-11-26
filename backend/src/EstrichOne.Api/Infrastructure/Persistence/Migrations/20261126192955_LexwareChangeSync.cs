using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LexwareChangeSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "change_sync",
                table: "lexware_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Both");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_change_sync_at",
                table: "lexware_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_change_sync_error",
                table: "lexware_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deviation_count",
                table: "lexware_contact_links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "deviations",
                table: "lexware_contact_links",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lexware_updated_at",
                table: "lexware_contact_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lexware_version",
                table: "lexware_contact_links",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "local_version",
                table: "lexware_contact_links",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_sync_at",
                table: "lexware_contact_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remote_state",
                table: "lexware_contact_links",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sync_attempts",
                table: "lexware_contact_links",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "sync_error",
                table: "lexware_contact_links",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "synced_at",
                table: "lexware_contact_links",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "synced_fields",
                table: "lexware_contact_links",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "change_sync",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "last_change_sync_at",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "last_change_sync_error",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "deviation_count",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "deviations",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "lexware_updated_at",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "lexware_version",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "local_version",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "next_sync_at",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "remote_state",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "sync_attempts",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "sync_error",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "synced_at",
                table: "lexware_contact_links");

            migrationBuilder.DropColumn(
                name: "synced_fields",
                table: "lexware_contact_links");
        }
    }
}
