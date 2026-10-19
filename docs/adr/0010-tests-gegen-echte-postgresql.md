# 0010 – Integrationstests gegen echte PostgreSQL

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
In-Memory-Datenbanken verhalten sich anders als PostgreSQL (Transaktionen, Constraints, `uuid`, Volltext).

## Entscheidung
Integrationstests starten PostgreSQL 17 per Testcontainers. Ist die Umgebungsvariable
`ESTRICHONE_TEST_DATABASE` gesetzt, wird stattdessen diese Datenbank verwendet (z. B. ohne Docker).

## Folgen
`dotnet test` braucht Docker oder eine bereitgestellte Testdatenbank. Die Tests sind langsamer, aber aussagekräftig.
