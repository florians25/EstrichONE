using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LexwareArticlesQuotationsReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "article_sync",
                table: "lexware_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "FromLexware");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_article_sync_at",
                table: "lexware_settings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_article_sync_error",
                table: "lexware_settings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "status_on_invoices_paid",
                table: "lexware_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "status_on_quotation_accepted",
                table: "lexware_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "review_reason",
                table: "bauakten",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "review_requested_at",
                table: "bauakten",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_stamp",
                table: "bauakten",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "catalog_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    article_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    gtin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    net_price = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    gross_price = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lexware_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lexware_version = table.Column<int>(type: "integer", nullable: true),
                    synced_fields = table.Column<string>(type: "text", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deviations = table.Column<string>(type: "text", nullable: true),
                    deviation_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    remote_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotation_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    introduction = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    remark = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    valid_until = table.Column<DateOnly>(type: "date", nullable: true),
                    tax_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payment_condition_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    payment_duration = table.Column<int>(type: "integer", nullable: true),
                    payment_discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    payment_discount_range = table.Column<int>(type: "integer", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lexware_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    send_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    send_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    send_uncertain = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotation_drafts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quotation_draft_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", precision: 14, scale: 3, nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    net_price = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quotation_draft_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_quotation_draft_lines_quotation_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "quotation_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalog_items_article_number",
                table: "catalog_items",
                column: "article_number");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_items_lexware_id",
                table: "catalog_items",
                column: "lexware_id",
                unique: true,
                filter: "lexware_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_catalog_items_title",
                table: "catalog_items",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_quotation_draft_lines_draft_id_position",
                table: "quotation_draft_lines",
                columns: new[] { "draft_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_quotation_drafts_bauakte_id",
                table: "quotation_drafts",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotation_drafts_contact_id",
                table: "quotation_drafts",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_quotation_drafts_lexware_id",
                table: "quotation_drafts",
                column: "lexware_id",
                unique: true,
                filter: "lexware_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalog_items");

            migrationBuilder.DropTable(
                name: "quotation_draft_lines");

            migrationBuilder.DropTable(
                name: "quotation_drafts");

            migrationBuilder.DropColumn(
                name: "article_sync",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "last_article_sync_at",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "last_article_sync_error",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "status_on_invoices_paid",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "status_on_quotation_accepted",
                table: "lexware_settings");

            migrationBuilder.DropColumn(
                name: "review_reason",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "review_requested_at",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "review_stamp",
                table: "bauakten");
        }
    }
}
