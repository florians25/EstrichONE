using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LexwareAutoAssign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auto_reason",
                table: "lexware_voucher_links",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "lexware_voucher_links",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<bool>(
                name: "auto_assign",
                table: "lexware_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "auto_assign_single_open",
                table: "lexware_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "lexware_auto_assign_blocks",
                columns: table => new
                {
                    lexware_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lexware_auto_assign_blocks", x => x.lexware_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lexware_auto_assign_blocks");

            migrationBuilder.DropColumn(
                name: "auto_reason",
                table: "lexware_voucher_links");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "lexware_voucher_links");

            migrationBuilder.DropColumn(
                name: "auto_assign",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "auto_assign_single_open",
                table: "lexware_settings");
        }
    }
}
