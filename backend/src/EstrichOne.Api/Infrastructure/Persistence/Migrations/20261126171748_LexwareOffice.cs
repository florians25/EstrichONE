using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LexwareOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "lexware_sync",
                table: "contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "lexware_contact_links",
                columns: table => new
                {
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lexware_contact_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lexware_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    lexware_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    origin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lexware_contact_links", x => x.contact_id);
                });

            migrationBuilder.CreateTable(
                name: "lexware_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    protected_api_key = table.Column<string>(type: "text", nullable: true),
                    base_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    new_customers_sync = table.Column<bool>(type: "boolean", nullable: false),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    organization_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lexware_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lexware_voucher_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    lexware_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    voucher_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sync_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lexware_voucher_links", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lexware_contact_links_lexware_contact_id",
                table: "lexware_contact_links",
                column: "lexware_contact_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lexware_voucher_links_bauakte_id",
                table: "lexware_voucher_links",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_lexware_voucher_links_lexware_id",
                table: "lexware_voucher_links",
                column: "lexware_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lexware_contact_links");

            migrationBuilder.DropTable(
                name: "lexware_settings");

            migrationBuilder.DropTable(
                name: "lexware_voucher_links");

            migrationBuilder.DropColumn(
                name: "lexware_sync",
                table: "contacts");
        }
    }
}
