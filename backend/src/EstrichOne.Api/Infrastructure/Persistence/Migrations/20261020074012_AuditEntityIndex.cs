using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEntityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_audit_events_entity_type_entity_id_occurred_at",
                table: "audit_events",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_audit_events_entity_type_entity_id_occurred_at",
                table: "audit_events");
        }
    }
}
