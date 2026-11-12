using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerArchiveAndDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                table: "contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "contacts",
                type: "timestamp with time zone",
                nullable: true);

            // Bestehende Installationen: Die Standardrolle „Büro und Bauleitung“ darf Kunden ohne Bezüge löschen (wie neue
            // Installationen). Anonymisieren (contacts.anonymize) bleibt dem Administrator vorbehalten.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission)
                SELECT id, 'contacts.delete' FROM roles WHERE id = '01a00000-0000-7000-8000-000000000002'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission IN ('contacts.delete', 'contacts.anonymize');");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "contacts");
        }
    }
}
