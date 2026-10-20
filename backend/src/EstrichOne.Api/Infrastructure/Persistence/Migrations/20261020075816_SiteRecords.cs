using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diary_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    weather = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    temperature_celsius = table.Column<int>(type: "integer", nullable: true),
                    workers_present = table.Column<int>(type: "integer", nullable: true),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diary_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_diary_entries_bauakten_bauakte_id",
                        column: x => x.bauakte_id,
                        principalTable: "bauakten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_diary_entries_user_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    performed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    corrects_inspection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remarks = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    not_required = table.Column<bool>(type: "boolean", nullable: false),
                    checks = table.Column<string>(type: "jsonb", nullable: false),
                    photo_file_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    binder = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    heated = table.Column<bool>(type: "boolean", nullable: true),
                    thickness_mm = table.Column<int>(type: "integer", nullable: true),
                    measured_cm_percent = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_inspections_bauakten_bauakte_id",
                        column: x => x.bauakte_id,
                        principalTable: "bauakten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_inspections_inspections_corrects_inspection_id",
                        column: x => x.corrects_inspection_id,
                        principalTable: "inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inspections_user_performed_by_user_id",
                        column: x => x.performed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_author_user_id",
                table: "diary_entries",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_diary_entries_bauakte_id_day",
                table: "diary_entries",
                columns: new[] { "bauakte_id", "day" });

            migrationBuilder.CreateIndex(
                name: "ix_inspections_bauakte_id_performed_at",
                table: "inspections",
                columns: new[] { "bauakte_id", "performed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inspections_corrects_inspection_id",
                table: "inspections",
                column: "corrects_inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_performed_by_user_id",
                table: "inspections",
                column: "performed_by_user_id");

            // Änderungsfeed für den Sync (siehe Migration Sync, record_change()).
            migrationBuilder.Sql("""
                CREATE TRIGGER diary_entries_change_log AFTER INSERT OR UPDATE OR DELETE ON diary_entries
                    FOR EACH ROW EXECUTE FUNCTION record_change('diaryEntry', 'bauakte_id');
                CREATE TRIGGER inspections_change_log AFTER INSERT OR UPDATE OR DELETE ON inspections
                    FOR EACH ROW EXECUTE FUNCTION record_change('inspection', 'bauakte_id');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS diary_entries_change_log ON diary_entries;
                DROP TRIGGER IF EXISTS inspections_change_log ON inspections;
                DELETE FROM change_log WHERE entity_type IN ('diaryEntry', 'inspection');
                """);

            migrationBuilder.DropTable(
                name: "diary_entries");

            migrationBuilder.DropTable(
                name: "inspections");
        }
    }
}
