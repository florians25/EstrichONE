using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveMailBackupTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archive_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    server_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    server_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ca_certificate_pem = table.Column<string>(type: "text", nullable: false),
                    ca_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    protected_token = table.Column<string>(type: "text", nullable: false),
                    paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paired_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_contact_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    auto_archive = table.Column<bool>(type: "boolean", nullable: false),
                    notify_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_connection", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "archive_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    retention_years = table.Column<int>(type: "integer", nullable: false),
                    trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retain_until = table.Column<DateOnly>(type: "date", nullable: true),
                    ledger_seq = table.Column<long>(type: "bigint", nullable: true),
                    ledger_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    archive_server_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "archive_rules",
                columns: table => new
                {
                    document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    retention_years = table.Column<int>(type: "integer", nullable: false),
                    trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_rules", x => x.document_type);
                    table.CheckConstraint("ck_archive_rules_years", "retention_years BETWEEN 1 AND 30");
                });

            migrationBuilder.CreateTable(
                name: "backup_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    server_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ca_certificate_pem = table.Column<string>(type: "text", nullable: false),
                    ca_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    protected_token = table.Column<string>(type: "text", nullable: false),
                    rest_path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rest_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    protected_rest_password = table.Column<string>(type: "text", nullable: false),
                    paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paired_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_contact_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backup_targets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mail_account",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false),
                    security = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_name = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    protected_password = table.Column<string>(type: "text", nullable: true),
                    from_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    from_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reply_to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    last_test_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_test_ok = table.Column<bool>(type: "boolean", nullable: true),
                    last_test_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_account", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mail_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    to = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    template_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    related_entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    attachments_json = table.Column<string>(type: "text", nullable: true),
                    attachment_names = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mail_templates",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mail_templates", x => x.key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_records_bauakte_id",
                table: "archive_records",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_archive_records_file_id",
                table: "archive_records",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_records_status_next_attempt_at",
                table: "archive_records",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_mail_log_created_at",
                table: "mail_log",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_mail_log_status_next_attempt_at",
                table: "mail_log",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_connection");

            migrationBuilder.DropTable(
                name: "archive_records");

            migrationBuilder.DropTable(
                name: "archive_rules");

            migrationBuilder.DropTable(
                name: "backup_targets");

            migrationBuilder.DropTable(
                name: "mail_account");

            migrationBuilder.DropTable(
                name: "mail_log");

            migrationBuilder.DropTable(
                name: "mail_templates");
        }
    }
}
