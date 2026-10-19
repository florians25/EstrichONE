# 0003 – PostgreSQL 17 mit EF Core, Migrationen als eigener Schritt

- **Status:** angenommen · **Datum:** 2026-10-19

## Kontext
Estrich Web änderte das Schema zur Laufzeit (101 Stellen). Der Stand einer Datenbank war dadurch nicht
nachvollziehbar.

## Entscheidung
PostgreSQL 17, EF Core mit Npgsql, Tabellen- und Spaltennamen in `snake_case`. Schemaänderungen nur über
EF-Migrationen im Repository. Die API ändert das Schema **nie** beim normalen Start; Migrationen laufen über
den Befehl `migrate` (in Docker als eigener Dienst vor dem API-Start).

## Folgen
- Jede Datenbank hat einen eindeutigen Migrationsstand (`__ef_migrations_history`).
- Migrationen werden in CI gegen eine echte PostgreSQL getestet.
