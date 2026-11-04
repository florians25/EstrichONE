using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OffloadStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offload_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    server_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    server_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ca_certificate_pem = table.Column<string>(type: "text", nullable: true),
                    ca_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    protected_token = table.Column<string>(type: "text", nullable: true),
                    protected_master_key = table.Column<string>(type: "text", nullable: false),
                    paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paired_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_contact_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    auto_when_low = table.Column<bool>(type: "boolean", nullable: false),
                    auto_by_rules = table.Column<bool>(type: "boolean", nullable: false),
                    threshold_percent = table.Column<int>(type: "integer", nullable: false),
                    threshold_free_gb = table.Column<int>(type: "integer", nullable: true),
                    min_age_months = table.Column<int>(type: "integer", nullable: false),
                    only_completed_bauakten = table.Column<bool>(type: "boolean", nullable: false),
                    include_photos = table.Column<bool>(type: "boolean", nullable: false),
                    include_videos = table.Column<bool>(type: "boolean", nullable: false),
                    include_documents = table.Column<bool>(type: "boolean", nullable: false),
                    require_backup = table.Column<bool>(type: "boolean", nullable: false),
                    cache_limit_mb = table.Column<int>(type: "integer", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    low_space_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unreachable_notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offload_connection", x => x.id);
                    table.CheckConstraint("ck_offload_threshold", "threshold_percent BETWEEN 1 AND 90");
                });

            migrationBuilder.CreateTable(
                name: "offloaded_blobs",
                columns: table => new
                {
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    cipher_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    cipher_bytes = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    offloaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    backup_request_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    backed_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_access_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offloaded_blobs", x => x.storage_key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offloaded_blobs_object_id",
                table: "offloaded_blobs",
                column: "object_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_offloaded_blobs_state",
                table: "offloaded_blobs",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "offload_connection");

            migrationBuilder.DropTable(
                name: "offloaded_blobs");
        }
    }
}
