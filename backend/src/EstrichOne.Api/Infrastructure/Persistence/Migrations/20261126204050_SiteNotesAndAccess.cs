using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SiteNotesAndAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "site_notes",
                table: "bauakten",
                type: "character varying(3000)",
                maxLength: 3000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            // „Zugang / Besonderheiten“ (project.siteAccess) geht in „Hinweise und Zugang“ (site_notes) auf; das eingebaute
            // Formularfeld entfällt. Beide Anweisungen sind wiederholbar.
            migrationBuilder.Sql(SiteNotesAccessMerge.Bauakten);
            migrationBuilder.Sql(SiteNotesAccessMerge.FormSchemas);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Die zusammengeführten Texte bleiben in site_notes; längere Texte als 2000 Zeichen lassen das Zurücksetzen scheitern.
            migrationBuilder.AlterColumn<string>(
                name: "site_notes",
                table: "bauakten",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(3000)",
                oldMaxLength: 3000,
                oldNullable: true);
        }
    }
}
