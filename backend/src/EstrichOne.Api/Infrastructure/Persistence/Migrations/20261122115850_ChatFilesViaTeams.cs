using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Ab 1.0.32 laufen Dateien und Fotos über Microsoft Teams: Anhänge im EstrichONE-Chat einmalig ausschalten
    /// (vorhandene bleiben sichtbar; Einstellungen › Module › Chat kann sie wieder erlauben).
    /// </summary>
    public partial class ChatFilesViaTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE chat_settings SET attachments_enabled = FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE chat_settings SET attachments_enabled = TRUE;");
        }
    }
}
