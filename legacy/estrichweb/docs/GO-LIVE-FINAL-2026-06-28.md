# EstrichWeb – Go-Live-Finalisierung (2026-06-28)

## DB-Zugangsdaten
`api/config/local.php` mit den neuen Zugangsdaten neu erstellt
(Host, Datenbankname und Benutzer entfernt – siehe docs/reengineering/17-sicherheit-und-datenschutz.md).
**Frisches JWT-Secret** generiert (altes war offengelegt) → alle Nutzer müssen sich
einmal neu anmelden.

## #1 Anfragen-Seite ans Standard-Design angeglichen
`inquiry-detail` nutzte einen Blau-Verlaufs-Hero. Jetzt weiße Standard-Karte
(Rahmen, Radius 8px, dezenter Icon-Chip, Standard-Chips) wie der Rest der App.
Rein CSS – Template/Funktion unverändert. Die Anfragen-Liste war bereits Standard.

## #2 Materialbestellungen ans Grunddesign angeglichen
Karten/Toolbar/KPIs hatten abweichende Radien (16–20px) und Farbverläufe.
Auf Standard-Tokens normalisiert (flach weiß, Radius 8px, Standard-Rahmen/Schatten),
8 CSS-Stellen.

## #3 Kundenportal finalisiert
- **Dokumente sind jetzt öffnen-/herunterladbar.** Neuer öffentlicher, abgesicherter
  Endpunkt `GET /api/public/portal/{token}/document/{id}` (prüft Token **und** PIN,
  liefert nur `customer_visible=1`-Dokumente des jeweiligen Kunden, streamt inline
  oder als Download). Im Portal je Dokument zwei Buttons (Öffnen / Herunterladen).
- Zugangsbrief: hartcodierter Fallback-Firmenname „Lies & Leis…“ entfernt.
- Die Zugänge-Seite (`customer-portal-admin`) war bereits vollständig
  (Zugang+PIN erzeugen, DIN-5008-Brief mit QR-Code).

## Login: Microsoft-Anmeldung entfernt
Button, Trenner, Hinweis sowie die zugehörige Logik (SSO-Ticket, Statusabfrage,
`microsoftLogin`) aus `login.component.ts` entfernt. Nur noch E-Mail + Passwort.

## #4 Leeres Schema + Installer (gegen aktuellen Stand)
- **`install/install.php`** – Browser-Installer: spielt `schema.sql` + **alle**
  Migrationen (`2026_*.sql`) + `seed.sql` (Rollen/Rechte) + Performance-Indizes
  ein (fehlertolerant, idempotent) und legt **Mandant + Admin** (`super_admin`,
  bcrypt) an. Bricht ab, wenn schon ein Benutzer existiert. **Danach `/install`
  löschen.**
- **`database/estrichweb_install_schema_2026_06_07.sql`** – komplettes leeres
  Schema (Struktur + alle Migrationen + Rollen/Rechte + Indizes), **einmaliger
  Erstimport** für eine neue, leere DB. Enthält keine Demodaten.
  Hinweis: nur für FRISCHE DBs (eine Migration setzt `audit_logs` neu auf) – nicht
  zum Aktualisieren einer bestehenden Produktiv-DB verwenden.
- **`database/2026_06_07_performance_indexes.sql`** – idempotente Indizes inkl. der
  neuen Tabellen (calls, material, portal) für gute Performance bei vielen Daten.

## Wichtige Hinweise
- **Build:** Wie immer konnte ich keinen `npm run build` ausführen – bitte lokal
  bauen (`npm ci && npm run build`), bevor du deployst. TS-Syntax aller geänderten
  Dateien wurde geprüft.
- **Installer:** PHP ließ sich hier nicht linten – bitte den Installer einmal gegen
  eine Test-/Staging-DB laufen lassen.
- **Sicherheit:** `/install` nach Gebrauch löschen, HTTPS erzwingen,
  `api/config/local.php` nie öffentlich zugänglich machen.
