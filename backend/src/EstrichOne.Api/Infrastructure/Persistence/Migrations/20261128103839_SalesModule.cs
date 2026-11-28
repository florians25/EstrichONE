using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Angebote (Beta) gehen im Modul „Verkauf“ auf: Tabellen umbenennen, Daten bleiben erhalten (Belegart Angebot).
            migrationBuilder.RenameTable(name: "quotation_drafts", newName: "sales_vouchers");
            migrationBuilder.RenameTable(name: "quotation_draft_lines", newName: "sales_voucher_lines");
            migrationBuilder.Sql("ALTER TABLE sales_vouchers RENAME CONSTRAINT pk_quotation_drafts TO pk_sales_vouchers;");
            migrationBuilder.Sql("ALTER TABLE sales_voucher_lines RENAME CONSTRAINT pk_quotation_draft_lines TO pk_sales_voucher_lines;");
            migrationBuilder.Sql("ALTER TABLE sales_voucher_lines RENAME CONSTRAINT fk_quotation_draft_lines_quotation_drafts_draft_id TO fk_sales_voucher_lines_sales_vouchers_draft_id;");
            migrationBuilder.RenameIndex(name: "ix_quotation_draft_lines_draft_id_position", table: "sales_voucher_lines", newName: "ix_sales_voucher_lines_draft_id_position");
            migrationBuilder.RenameIndex(name: "ix_quotation_drafts_bauakte_id", table: "sales_vouchers", newName: "ix_sales_vouchers_bauakte_id");
            migrationBuilder.RenameIndex(name: "ix_quotation_drafts_contact_id", table: "sales_vouchers", newName: "ix_sales_vouchers_contact_id");
            migrationBuilder.RenameIndex(name: "ix_quotation_drafts_lexware_id", table: "sales_vouchers", newName: "ix_sales_vouchers_lexware_id");

            migrationBuilder.AddColumn<string>(
                name: "voucher_type",
                table: "sales_vouchers",
                type: "character varying(30)", maxLength: 30,
                nullable: false, defaultValue: "quotation");

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "sales_vouchers",
                type: "character varying(20)", maxLength: 20,
                nullable: false, defaultValue: "standard");

            migrationBuilder.AddColumn<DateOnly>(
                name: "voucher_date",
                table: "sales_vouchers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "service_date",
                table: "sales_vouchers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "service_end_date",
                table: "sales_vouchers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "preceding_id",
                table: "sales_vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "preceding_lexware_id",
                table: "sales_vouchers",
                type: "character varying(100)", maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "down_payment_percent",
                table: "sales_vouchers",
                type: "numeric(5,2)", precision: 5, scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "finalized",
                table: "sales_vouchers",
                type: "boolean",
                nullable: false, defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "lexware_number",
                table: "sales_vouchers",
                type: "character varying(100)", maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lexware_status",
                table: "sales_vouchers",
                type: "character varying(20)", maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "open_amount",
                table: "sales_vouchers",
                type: "numeric(14,2)", precision: 14, scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_status",
                table: "sales_vouchers",
                type: "character varying(30)", maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "dunning_level",
                table: "sales_vouchers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lexware_synced_at",
                table: "sales_vouchers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deduction_voucher_id",
                table: "sales_voucher_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_vouchers_preceding_id",
                table: "sales_vouchers",
                column: "preceding_id");

            // Gesendete Angebote: Status „Entwurf“ in Lexware Office, bis der Abgleich den aktuellen Stand liest.
            migrationBuilder.Sql("UPDATE sales_vouchers SET lexware_status = 'draft' WHERE state = 'Sent' AND lexware_status IS NULL;");

            // War „Angebote (Beta)“ eingeschaltet, bleibt es als „Verkauf: Angebote“ an; dafür wird „Verkauf“ eingeschaltet.
            migrationBuilder.Sql("""
                UPDATE system_settings SET data = jsonb_set(data, '{modules,sales}', 'true'::jsonb, true)
                WHERE data ? 'modules' AND (data->'modules'->>'quotations') = 'true';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_sales_vouchers_preceding_id", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "voucher_type", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "purpose", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "voucher_date", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "service_date", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "service_end_date", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "preceding_id", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "preceding_lexware_id", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "down_payment_percent", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "finalized", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "lexware_number", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "lexware_status", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "open_amount", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "payment_status", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "dunning_level", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "lexware_synced_at", table: "sales_vouchers");
            migrationBuilder.DropColumn(name: "deduction_voucher_id", table: "sales_voucher_lines");
            migrationBuilder.RenameIndex(name: "ix_sales_voucher_lines_draft_id_position", table: "sales_voucher_lines", newName: "ix_quotation_draft_lines_draft_id_position");
            migrationBuilder.RenameIndex(name: "ix_sales_vouchers_bauakte_id", table: "sales_vouchers", newName: "ix_quotation_drafts_bauakte_id");
            migrationBuilder.RenameIndex(name: "ix_sales_vouchers_contact_id", table: "sales_vouchers", newName: "ix_quotation_drafts_contact_id");
            migrationBuilder.RenameIndex(name: "ix_sales_vouchers_lexware_id", table: "sales_vouchers", newName: "ix_quotation_drafts_lexware_id");
            migrationBuilder.Sql("ALTER TABLE sales_voucher_lines RENAME CONSTRAINT fk_sales_voucher_lines_sales_vouchers_draft_id TO fk_quotation_draft_lines_quotation_drafts_draft_id;");
            migrationBuilder.Sql("ALTER TABLE sales_voucher_lines RENAME CONSTRAINT pk_sales_voucher_lines TO pk_quotation_draft_lines;");
            migrationBuilder.Sql("ALTER TABLE sales_vouchers RENAME CONSTRAINT pk_sales_vouchers TO pk_quotation_drafts;");
            migrationBuilder.RenameTable(name: "sales_voucher_lines", newName: "quotation_draft_lines");
            migrationBuilder.RenameTable(name: "sales_vouchers", newName: "quotation_drafts");
        }
    }
}
