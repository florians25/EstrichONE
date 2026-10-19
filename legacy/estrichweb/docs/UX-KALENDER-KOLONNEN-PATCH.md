# UX/Kalender/Kolonnen-Patch

Dieser Stand ersetzt die alte Inline-Erfassung durch Dialoge und ergänzt einen fachlichen Kolonnen-Wochenplaner.

## Geändert

- Allgemeine CRUD-Module: Erfassung und Bearbeitung über Modal-Dialoge statt Seiten-Spaltendarstellung.
- Mobile UI: helle Kontraste, Bottom-Navigation, Floating-Action-Button, responsive Kartenansicht für Tabellen.
- Kalender: Google-Kalender-ähnlicher Aufbau mit linker Mini-Monatsnavigation, Heute/Zurück/Vor, Tag/Woche/Monat/Agenda und ganztägiger Zeile.
- Termine sind projektgebunden: `calendar_events.project_id` und Projekt-Dropdown im Termin-Dialog.
- Neuer Kolonnen-Wochenplaner unter `/kolonnen-wochenplaner`.
- Kolonnen-Wochenplaner: Wochenraster, Kolonnen-Zeilen, Drag & Drop, Auftragsfilter und Dropdown mit letzten Aufträgen.
- Planung ist auftragsverknüpft: `planning_assignments.project_id`.

## Upload auf STRATO

Für diesen Patch mindestens ersetzen:

```text
.htaccess
/public
/api
/database
```

Wenn die Datenbank bereits existiert, zusätzlich ausführen:

```text
/database/2026_05_29_project_bound_calendar_crew_planner.sql
```

Bei "Duplicate column" oder "Duplicate key" ist die jeweilige Änderung bereits vorhanden.

## Neue Routen

```text
/kalender-board
/kolonnen-wochenplaner
```
