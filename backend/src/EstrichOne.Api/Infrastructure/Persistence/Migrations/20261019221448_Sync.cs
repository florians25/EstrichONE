using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_operations",
                columns: table => new
                {
                    op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_operations", x => x.op_id);
                    table.ForeignKey(
                        name: "fk_sync_operations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_operations_status",
                table: "sync_operations",
                column: "status",
                filter: "status = 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_sync_operations_user_id_processed_at",
                table: "sync_operations",
                columns: new[] { "user_id", "processed_at" });
            // Änderungsfeed (docs/sync-protokoll.md, Abschnitt 5): je Entität der letzte Stand, gepflegt per Trigger
            // in derselben Transaktion. Sortierung nach Transaktions-ID; ausgeliefert wird nur, was unter
            // pg_snapshot_xmin liegt – so überholt keine später festgeschriebene Änderung den Cursor.
            migrationBuilder.Sql("""
                CREATE SEQUENCE change_log_seq;
                CREATE TABLE change_log (
                    entity_type varchar(50) NOT NULL,
                    entity_id uuid NOT NULL,
                    bauakte_id uuid NULL,
                    deleted boolean NOT NULL,
                    txid xid8 NOT NULL,
                    seq bigint NOT NULL,
                    CONSTRAINT pk_change_log PRIMARY KEY (entity_type, entity_id)
                );
                CREATE INDEX ix_change_log_txid_seq ON change_log (txid, seq);

                CREATE FUNCTION record_change() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE
                    rec record;
                    scope uuid;
                    is_deleted boolean;
                BEGIN
                    IF TG_OP = 'DELETE' THEN rec := OLD; ELSE rec := NEW; END IF;
                    IF TG_ARGV[1] <> '' THEN
                        EXECUTE format('SELECT ($1).%I', TG_ARGV[1]) USING rec INTO scope;
                    END IF;
                    is_deleted := TG_OP = 'DELETE' OR COALESCE((to_jsonb(rec) ->> 'deleted_at') IS NOT NULL, false);
                    INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    VALUES (TG_ARGV[0], rec.id, scope, is_deleted, pg_current_xact_id(), nextval('change_log_seq'))
                    ON CONFLICT (entity_type, entity_id) DO UPDATE
                        SET bauakte_id = EXCLUDED.bauakte_id, deleted = EXCLUDED.deleted, txid = EXCLUDED.txid, seq = EXCLUDED.seq;
                    RETURN NULL;
                END;
                $$;

                CREATE TRIGGER bauakten_change_log AFTER INSERT OR UPDATE OR DELETE ON bauakten
                    FOR EACH ROW EXECUTE FUNCTION record_change('bauakte', 'id');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'bauakte', id, id, deleted_at IS NOT NULL, pg_current_xact_id(), nextval('change_log_seq') FROM bauakten;
                CREATE TRIGGER notes_change_log AFTER INSERT OR UPDATE OR DELETE ON notes
                    FOR EACH ROW EXECUTE FUNCTION record_change('note', 'bauakte_id');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'note', id, bauakte_id, deleted_at IS NOT NULL, pg_current_xact_id(), nextval('change_log_seq') FROM notes;
                CREATE TRIGGER files_change_log AFTER INSERT OR UPDATE OR DELETE ON files
                    FOR EACH ROW EXECUTE FUNCTION record_change('file', 'bauakte_id');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'file', id, bauakte_id, deleted_at IS NOT NULL, pg_current_xact_id(), nextval('change_log_seq') FROM files;
                CREATE TRIGGER work_items_change_log AFTER INSERT OR UPDATE OR DELETE ON work_items
                    FOR EACH ROW EXECUTE FUNCTION record_change('workItem', 'bauakte_id');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'workItem', id, bauakte_id, deleted_at IS NOT NULL, pg_current_xact_id(), nextval('change_log_seq') FROM work_items;
                CREATE TRIGGER contacts_change_log AFTER INSERT OR UPDATE OR DELETE ON contacts
                    FOR EACH ROW EXECUTE FUNCTION record_change('contact', '');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'contact', id, NULL::uuid, deleted_at IS NOT NULL, pg_current_xact_id(), nextval('change_log_seq') FROM contacts;
                CREATE TRIGGER screed_types_change_log AFTER INSERT OR UPDATE OR DELETE ON screed_types
                    FOR EACH ROW EXECUTE FUNCTION record_change('screedType', '');
                INSERT INTO change_log (entity_type, entity_id, bauakte_id, deleted, txid, seq)
                    SELECT 'screedType', id, NULL::uuid, false, pg_current_xact_id(), nextval('change_log_seq') FROM screed_types;
                """);
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS bauakten_change_log ON bauakten; DROP TRIGGER IF EXISTS notes_change_log ON notes; DROP TRIGGER IF EXISTS files_change_log ON files; DROP TRIGGER IF EXISTS work_items_change_log ON work_items; DROP TRIGGER IF EXISTS contacts_change_log ON contacts; DROP TRIGGER IF EXISTS screed_types_change_log ON screed_types; DROP FUNCTION IF EXISTS record_change(); DROP TABLE IF EXISTS change_log; DROP SEQUENCE IF EXISTS change_log_seq;");

            migrationBuilder.DropTable(
                name: "sync_operations");
        }
    }
}
