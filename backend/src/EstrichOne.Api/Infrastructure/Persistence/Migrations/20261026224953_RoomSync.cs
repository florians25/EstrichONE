using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Räume und Flächen im Änderungsfeed (App: „Räume & Flächen“ offline); siehe Migration Sync, record_change().
            migrationBuilder.Sql("""
                CREATE TRIGGER rooms_change_log AFTER INSERT OR UPDATE OR DELETE ON rooms
                    FOR EACH ROW EXECUTE FUNCTION record_change('room', 'bauakte_id');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'room', id, bauakte_id, deleted_at IS NOT NULL, pg_current_xact_id(), nextval('change_log_seq') FROM rooms
                    ON CONFLICT (entity_type, entity_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS rooms_change_log ON rooms; DELETE FROM change_log WHERE entity_type = 'room';");
        }
    }
}
