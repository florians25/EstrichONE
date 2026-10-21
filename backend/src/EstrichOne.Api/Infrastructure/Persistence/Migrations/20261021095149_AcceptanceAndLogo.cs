using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcceptanceAndLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "acceptance_protocols",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    client_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contractor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    scope = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    room_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    photo_file_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    defect_work_item_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    client_signature = table.Column<byte[]>(type: "bytea", nullable: true),
                    client_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    contractor_signature = table.Column<byte[]>(type: "bytea", nullable: true),
                    contractor_signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    customer_link_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    customer_decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    customer_decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_decision_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    customer_comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    customer_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pdf_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceptance_protocols", x => x.id);
                    table.ForeignKey(
                        name: "fk_acceptance_protocols_bauakten_bauakte_id",
                        column: x => x.bauakte_id,
                        principalTable: "bauakten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_acceptance_protocols_user_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_logo",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    png = table.Column<byte[]>(type: "bytea", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_logo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_protocols_bauakte_id",
                table: "acceptance_protocols",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_protocols_customer_token_hash",
                table: "acceptance_protocols",
                column: "customer_token_hash",
                unique: true,
                filter: "customer_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_acceptance_protocols_performed_by_user_id",
                table: "acceptance_protocols",
                column: "performed_by_user_id");

            // Änderungsfeed für den Sync (siehe Migration Sync, record_change()).
            migrationBuilder.Sql("""
                CREATE TRIGGER acceptance_protocols_change_log AFTER INSERT OR UPDATE OR DELETE ON acceptance_protocols
                    FOR EACH ROW EXECUTE FUNCTION record_change('acceptance', 'bauakte_id');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS acceptance_protocols_change_log ON acceptance_protocols; DELETE FROM change_log WHERE entity_type = 'acceptance';");

            migrationBuilder.DropTable(
                name: "acceptance_protocols");

            migrationBuilder.DropTable(
                name: "company_logo");
        }
    }
}
