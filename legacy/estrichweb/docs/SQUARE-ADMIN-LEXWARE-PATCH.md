# Patch: eckiges Bürodesign, Admin-Verwaltung, Lexware Office

## Enthalten

- Kompaktere Seitenköpfe ohne überladene Hero-Flächen
- neue aufklappbare Sidebar mit Arbeitsbereichen statt langer Modulliste
- vollständig eckigere Buttons, Karten, Tabellen, Dialoge und Formularfelder
- keine Dark-Mode-Farben
- neue Route `/admin-verwaltung` für Benutzer, Rollen, Geräte und Audit-Übersicht
- neue Route `/lexware-integration` für Lexware Office REST-API-Konfiguration, Kunden-Sync und Auftragsverknüpfung
- Backend-Endpunkte unter `/api/integrations/lexware/*`
- Tabellen `lexware_connections` und `lexware_entity_links`

## Bestehende Datenbank patchen

Für bestehende Installationen Datei ausführen:

`database/2026_05_29_square_admin_lexware_patch.sql`

Falls einzelne ALTER-TABLE-Befehle melden, dass die Spalte bereits existiert, ist das unkritisch. Die API legt fehlende Lexware-Tabellen/Spalten zusätzlich beim Aufruf der Integration automatisch an.
