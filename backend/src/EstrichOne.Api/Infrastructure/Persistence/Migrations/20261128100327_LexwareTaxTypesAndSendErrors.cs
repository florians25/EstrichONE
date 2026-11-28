using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LexwareTaxTypesAndSendErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "send_error_fix",
                table: "quotation_drafts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "send_error_technical",
                table: "quotation_drafts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "profile_small_business",
                table: "lexware_settings",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "profile_tax_type",
                table: "lexware_settings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejected_tax_types",
                table: "lexware_settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tax_types_checked_at",
                table: "lexware_settings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "send_error_fix",
                table: "quotation_drafts");

            migrationBuilder.DropColumn(
                name: "send_error_technical",
                table: "quotation_drafts");

            migrationBuilder.DropColumn(
                name: "profile_small_business",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "profile_tax_type",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "rejected_tax_types",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "tax_types_checked_at",
                table: "lexware_settings");
        }
    }
}
