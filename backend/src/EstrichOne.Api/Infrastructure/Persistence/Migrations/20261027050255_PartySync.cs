using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartySync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Beteiligte (Bauherr, Bauleiter, Architekt …) im Änderungsfeed, damit die App sie offline zeigt; siehe Migration Sync,
            // record_change(). Beteiligte werden hart gelöscht – das meldet der Trigger als gelöscht.
            migrationBuilder.Sql("""
                CREATE TRIGGER bauakte_parties_change_log AFTER INSERT OR UPDATE OR DELETE ON bauakte_parties
                    FOR EACH ROW EXECUTE FUNCTION record_change('bauakteParty', 'bauakte_id');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'bauakteParty', id, bauakte_id, false, pg_current_xact_id(), nextval('change_log_seq') FROM bauakte_parties
                    ON CONFLICT (entity_type, entity_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS bauakte_parties_change_log ON bauakte_parties; DELETE FROM change_log WHERE entity_type = 'bauakteParty';");
        }
    }
}
