# Patch: Lexware Auto-Sync, Bauakte-Karte, mobile Optimierung

## Fachliche Änderungen
- Beim Anlegen eines Auftrags wird kein Kalendertermin mehr abgefragt und keine Plantafel-Einplanung automatisch erzeugt.
- Termine werden innerhalb der Auftrags-/Baustellenakte über den Button „Termin“ angelegt.
- Die Auftragsakte enthält eine integrierte Kartenansicht der Baustellenadresse.
- Lexware Office synchronisiert mehr Daten: Kontakte/Kunden, Belege, Rechnungen und Angebote werden als `lexware_synced_records` gespeichert.
- Auto-Sync läuft alle 4 Minuten, wenn ein berechtigter Admin im System angemeldet ist. Zusätzlich kann ein Server-/STRATO-Cron später gegen `/api/integrations/lexware/auto-sync` ergänzt werden.

## Datenbank
Bestehende Installation aktualisieren mit:
`database/2026_05_30_lexware_autosync_map_mobile_patch.sql`

## Upload
Auf STRATO mindestens ersetzen:
- `/public`
- `/api`
- `/database`
- `/docs`

Danach Browser-Cache, Service Worker und Local Storage löschen.
