using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Purchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchase_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    data_json = table.Column<string>(type: "jsonb", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    invoice_date = table.Column<DateOnly>(type: "date", nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lexware_file_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    send_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    send_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_supplier_links",
                columns: table => new
                {
                    lexware_contact_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_supplier_links", x => x.lexware_contact_id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_voucher_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lexware_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    voucher_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    voucher_date = table.Column<DateOnly>(type: "date", nullable: true),
                    gross_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    lexware_contact_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_voucher_links", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_documents_bauakte_id",
                table: "purchase_documents",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_documents_sha256",
                table: "purchase_documents",
                column: "sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_voucher_links_bauakte_id",
                table: "purchase_voucher_links",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_voucher_links_lexware_id",
                table: "purchase_voucher_links",
                column: "lexware_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "purchase_documents");

            migrationBuilder.DropTable(
                name: "purchase_supplier_links");

            migrationBuilder.DropTable(
                name: "purchase_voucher_links");
        }
    }
}
