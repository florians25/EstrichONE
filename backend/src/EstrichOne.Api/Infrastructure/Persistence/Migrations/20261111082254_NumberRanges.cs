using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NumberRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "work_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "suppliers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inventory_number",
                table: "resources",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "materials",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "crews",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "number",
                table: "calls",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "number_range_counters",
                columns: table => new
                {
                    record_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_range_counters", x => new { x.record_type, x.period });
                });

            migrationBuilder.CreateTable(
                name: "number_ranges",
                columns: table => new
                {
                    record_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    pattern = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    range_start = table.Column<long>(type: "bigint", nullable: false),
                    range_end = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_ranges", x => x.record_type);
                });

            // Einstellungen der Nummernkreise: Standardmuster; Bauakten und Kunden übernehmen die bisherigen Muster aus den Systemeinstellungen.
            migrationBuilder.Sql("""
                CREATE FUNCTION pg_temp.eo_capacity(pattern text) RETURNS bigint LANGUAGE sql AS
                $$ SELECT (10::numeric ^ length(substring(pattern FROM '\{(N+)\}')))::bigint - 1 $$;

                CREATE FUNCTION pg_temp.eo_format(pattern text, yr int, n bigint) RETURNS text LANGUAGE plpgsql AS $$
                DECLARE
                    digits int := length(substring(pattern FROM '\{(N+)\}'));
                    counter text := CASE WHEN length(n::text) >= digits THEN n::text ELSE lpad(n::text, digits, '0') END;
                BEGIN
                    RETURN regexp_replace(replace(replace(pattern, '{JJJJ}', yr::text), '{JJ}', lpad((yr % 100)::text, 2, '0')), '\{N+\}', counter);
                END $$;

                WITH settings AS (SELECT data -> 'numbering' AS n FROM system_settings LIMIT 1),
                legacy AS (
                    SELECT coalesce((SELECT n ->> 'bauaktePattern' FROM settings WHERE n ->> 'bauaktePattern' LIKE '%{N%'), '{JJJJ}-{NNN}') AS bauakte,
                           coalesce((SELECT n ->> 'customerPattern' FROM settings WHERE n ->> 'customerPattern' ~ '\{N+\}'), '60{NNNN}') AS customer)
                INSERT INTO number_ranges (record_type, pattern, range_start, range_end, version, created_at, updated_at)
                SELECT d.key, d.pattern, 1, CASE WHEN position('{JJ' IN d.pattern) > 0 THEN NULL ELSE pg_temp.eo_capacity(d.pattern) END, 1, now(), now()
                FROM legacy, LATERAL (VALUES
                    ('bauakte', legacy.bauakte), ('customer', legacy.customer), ('supplier', '70{NNNN}'), ('employee', '10{NNN}'),
                    ('resource', '20{NNN}'), ('crew', 'K{NN}'), ('material', '50{NNNN}'), ('order', 'B-{JJJJ}-{NNNN}'), ('inquiry', 'A-{JJJJ}-{NNNN}'),
                    ('task', 'AU-{JJJJ}-{NNNN}'), ('defect', 'M-{JJJJ}-{NNNN}'), ('ticket', 'T-{JJJJ}-{NNNN}'), ('feedback', 'F-{JJJJ}-{NNNN}')) AS d(key, pattern);
                """);

            // Bisherige Zähler übernehmen (je Jahr bzw. fortlaufend), damit keine Nummer doppelt vergeben wird.
            migrationBuilder.Sql("""
                INSERT INTO number_range_counters (record_type, period, last_number)
                SELECT 'customer', CASE WHEN position('{JJ' IN r.pattern) > 0 THEN extract(year FROM now() AT TIME ZONE 'Europe/Berlin')::int ELSE 0 END, s.last_number
                FROM customer_number_sequences s, number_ranges r WHERE s.id = 1 AND r.record_type = 'customer';

                INSERT INTO number_range_counters (record_type, period, last_number)
                SELECT 'bauakte', year, last_number FROM bauakte_number_sequences, number_ranges r
                WHERE r.record_type = 'bauakte' AND position('{JJ' IN r.pattern) > 0;
                INSERT INTO number_range_counters (record_type, period, last_number)
                SELECT 'bauakte', 0, max(last_number) FROM bauakte_number_sequences, number_ranges r
                WHERE r.record_type = 'bauakte' AND position('{JJ' IN r.pattern) = 0 HAVING count(*) > 0;

                INSERT INTO number_range_counters (record_type, period, last_number) SELECT 'order', year, last_number FROM order_number_sequences;
                INSERT INTO number_range_counters (record_type, period, last_number) SELECT 'ticket', year, last_number FROM ticket_number_sequences;
                INSERT INTO number_range_counters (record_type, period, last_number) SELECT 'feedback', year, last_number FROM feedback_number_sequences;
                """);

            // Vorhandene Datensätze ohne Nummer erhalten Nummern in Anlagereihenfolge (Vorgänge im Jahr ihrer Anlage).
            // Vorhandene Nummern bleiben unverändert und werden übersprungen; ist ein Bereich ausgeschöpft, bleiben die
            // übrigen Datensätze ohne Nummer (Hinweis in den Einstellungen), die Migration schlägt nicht fehl.
            migrationBuilder.Sql("""
                CREATE FUNCTION pg_temp.eo_number(key text, tbl text, col text, filter text) RETURNS void LANGUAGE plpgsql AS $$
                DECLARE
                    r record;
                    pat text;
                    st bigint;
                    en bigint;
                    yr int;
                    per int;
                    cur bigint;
                    num text;
                    taken boolean;
                BEGIN
                    SELECT pattern, range_start, range_end INTO pat, st, en FROM number_ranges WHERE record_type = key;
                    FOR r IN EXECUTE format('SELECT id, created_at FROM %I WHERE %I IS NULL %s ORDER BY created_at, id', tbl, col, filter) LOOP
                        yr := extract(year FROM r.created_at AT TIME ZONE 'Europe/Berlin')::int;
                        per := CASE WHEN position('{JJ' IN pat) > 0 THEN yr ELSE 0 END;
                        LOOP
                            INSERT INTO number_range_counters (record_type, period, last_number) VALUES (key, per, st)
                            ON CONFLICT (record_type, period) DO UPDATE SET last_number = greatest(number_range_counters.last_number + 1, st)
                            RETURNING last_number INTO cur;
                            IF en IS NOT NULL AND cur > en THEN
                                UPDATE number_range_counters SET last_number = en WHERE record_type = key AND period = per;
                                RETURN;
                            END IF;
                            num := pg_temp.eo_format(pat, yr, cur);
                            EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I WHERE %I = $1)', tbl, col) INTO taken USING num;
                            EXIT WHEN NOT taken;
                        END LOOP;
                        EXECUTE format('UPDATE %I SET %I = $1, version = version + 1 WHERE id = $2', tbl, col) USING num, r.id;
                    END LOOP;
                END $$;

                SELECT pg_temp.eo_number('customer', 'contacts', 'customer_number', '');
                SELECT pg_temp.eo_number('supplier', 'suppliers', 'number', 'AND deleted_at IS NULL');
                SELECT pg_temp.eo_number('employee', 'employees', 'personnel_number', '');
                SELECT pg_temp.eo_number('resource', 'resources', 'inventory_number', '');
                SELECT pg_temp.eo_number('crew', 'crews', 'number', '');
                SELECT pg_temp.eo_number('material', 'materials', 'number', 'AND deleted_at IS NULL');
                SELECT pg_temp.eo_number('inquiry', 'calls', 'number', 'AND kind = ''Inquiry'' AND deleted_at IS NULL');
                SELECT pg_temp.eo_number('task', 'work_items', 'number', 'AND kind = ''Task'' AND deleted_at IS NULL');
                SELECT pg_temp.eo_number('defect', 'work_items', 'number', 'AND kind = ''Defect'' AND deleted_at IS NULL');
                """);

            migrationBuilder.CreateIndex(
                name: "ix_work_items_number",
                table: "work_items",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_number",
                table: "suppliers",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_resources_inventory_number",
                table: "resources",
                column: "inventory_number",
                unique: true,
                filter: "inventory_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_materials_number",
                table: "materials",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_crews_number",
                table: "crews",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_calls_number",
                table: "calls",
                column: "number",
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.DropTable(name: "bauakte_number_sequences");
            migrationBuilder.DropTable(name: "customer_number_sequences");
            migrationBuilder.DropTable(name: "feedback_number_sequences");
            migrationBuilder.DropTable(name: "order_number_sequences");
            migrationBuilder.DropTable(name: "ticket_number_sequences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_range_counters");

            migrationBuilder.DropTable(
                name: "number_ranges");

            migrationBuilder.DropIndex(
                name: "ix_work_items_number",
                table: "work_items");

            migrationBuilder.DropIndex(
                name: "ix_suppliers_number",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "ix_resources_inventory_number",
                table: "resources");

            migrationBuilder.DropIndex(
                name: "ix_materials_number",
                table: "materials");

            migrationBuilder.DropIndex(
                name: "ix_crews_number",
                table: "crews");

            migrationBuilder.DropIndex(
                name: "ix_calls_number",
                table: "calls");

            migrationBuilder.DropColumn(
                name: "number",
                table: "work_items");

            migrationBuilder.DropColumn(
                name: "number",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "inventory_number",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "number",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "number",
                table: "crews");

            migrationBuilder.DropColumn(
                name: "number",
                table: "calls");

            migrationBuilder.CreateTable(
                name: "bauakte_number_sequences",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bauakte_number_sequences", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "customer_number_sequences",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feedback_number_sequences",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_number_sequences", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "order_number_sequences",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_number_sequences", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "ticket_number_sequences",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_number_sequences", x => x.year);
                });
        }
    }
}
