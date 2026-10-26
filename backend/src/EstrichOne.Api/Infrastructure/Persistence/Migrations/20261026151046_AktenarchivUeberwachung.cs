using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AktenarchivUeberwachung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dossier_after_months",
                table: "archive_connection",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "archive_dossiers",
                columns: table => new
                {
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    files_total = table.Column<int>(type: "integer", nullable: false),
                    files_done = table.Column<int>(type: "integer", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    available_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unlocked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unlocked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manifest_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_dossiers", x => x.bauakte_id);
                });

            migrationBuilder.CreateTable(
                name: "monitoring_samples",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_bytes = table.Column<long>(type: "bigint", nullable: false),
                    total_bytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_samples", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_samples_source_at",
                table: "monitoring_samples",
                columns: new[] { "source", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_dossiers");

            migrationBuilder.DropTable(
                name: "monitoring_samples");

            migrationBuilder.DropColumn(
                name: "dossier_after_months",
                table: "archive_connection");
        }
    }
}
