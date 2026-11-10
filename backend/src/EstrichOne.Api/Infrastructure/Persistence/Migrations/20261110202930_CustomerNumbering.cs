using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            // Bestehende Kunden ohne Nummer erhalten Nummern im Standardmuster „60{NNNN}“ (600001 bis 609999) in
            // Anlagereihenfolge; vorhandene Nummern bleiben. Der Zähler beginnt hinter der höchsten vorhandenen 60xxxx-Nummer.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    start bigint := (SELECT coalesce(max(substring(customer_number FROM '^60([0-9]{4})$')::bigint), 0)
                                     FROM contacts WHERE customer_number ~ '^60[0-9]{4}$');
                    missing bigint := (SELECT count(*) FROM contacts WHERE customer_number IS NULL);
                BEGIN
                    -- Mehr Kunden als Nummern frei: die ältesten erhalten Nummern, die übrigen bleiben ohne (Hinweis in den Einstellungen).
                    missing := least(missing, 9999 - start);
                    UPDATE contacts c
                    SET customer_number = '60' || lpad((start + todo.n)::text, 4, '0'), version = c.version + 1
                    FROM (SELECT id, row_number() OVER (ORDER BY created_at, id) AS n FROM contacts WHERE customer_number IS NULL) todo
                    WHERE c.id = todo.id AND todo.n <= missing;
                    INSERT INTO customer_number_sequences (id, last_number) VALUES (1, start + missing);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_number_sequences");
        }
    }
}
