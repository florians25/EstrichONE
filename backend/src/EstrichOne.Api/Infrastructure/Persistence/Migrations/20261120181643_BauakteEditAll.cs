using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BauakteEditAll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // „Alle Bauakten bearbeiten“: Bisher durfte jede Rolle mit „Alle Bauakten sehen“ und „Bauakten bearbeiten“ auch jede
            // Bauakte ändern (z. B. „Büro und Bauleitung“). Damit sich für sie nichts ändert, erhalten sie das neue Recht. Alle
            // anderen dürfen künftig nur zugeteilte Bauakten ändern; sehen dürfen sie alle.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission)
                SELECT r.role_id, 'bauakten.edit_all' FROM role_permissions r
                WHERE r.permission = 'bauakten.read_all'
                  AND EXISTS (SELECT 1 FROM role_permissions e WHERE e.role_id = r.role_id AND e.permission = 'bauakten.edit')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission = 'bauakten.edit_all';");
        }
    }
}
