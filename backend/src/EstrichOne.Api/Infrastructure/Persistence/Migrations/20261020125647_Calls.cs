using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Calls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "calls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caller_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    caller_phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    caller_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    follow_up = table.Column<bool>(type: "boolean", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    next_step = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    inquiry_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    area_square_meters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    screed_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    desired_start = table.Column<DateOnly>(type: "date", nullable: true),
                    site_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    site_postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    site_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    converted_bauakte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    handled_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    called_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calls", x => x.id);
                    table.ForeignKey(
                        name: "fk_calls_bauakten_bauakte_id",
                        column: x => x.bauakte_id,
                        principalTable: "bauakten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_calls_bauakten_converted_bauakte_id",
                        column: x => x.converted_bauakte_id,
                        principalTable: "bauakten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_calls_contact_customer_id",
                        column: x => x.customer_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_calls_user_handled_by_user_id",
                        column: x => x.handled_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calls_bauakte_id",
                table: "calls",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_calls_converted_bauakte_id",
                table: "calls",
                column: "converted_bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_calls_customer_id",
                table: "calls",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_calls_due_date",
                table: "calls",
                column: "due_date",
                filter: "follow_up");

            migrationBuilder.CreateIndex(
                name: "ix_calls_handled_by_user_id",
                table: "calls",
                column: "handled_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_calls_kind_called_at",
                table: "calls",
                columns: new[] { "kind", "called_at" });

            migrationBuilder.CreateIndex(
                name: "ix_calls_kind_inquiry_status",
                table: "calls",
                columns: new[] { "kind", "inquiry_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calls");
        }
    }
}
