# 01 – Bestandsaufnahme Estrich Web

**Untersuchter Stand:** `estrichweb-source-2026-08-23.zip` („Runde 66“), übernommen nach
[`legacy/estrichweb/`](../../legacy/estrichweb/). Alle Pfadangaben in diesem Dokument beziehen sich auf diesen Ordner.

**Methode:** Ich habe den Quellcode gelesen und statisch ausgewertet: Routen, Rechteprüfung, Authentifizierung,
Datenbankschema, Migrationen, Angular-Struktur und Stylesheet. **Ich habe die Anwendung nicht gestartet**, denn es
gab weder Datenbank noch PHP-Laufzeit noch Produktivdaten. Aussagen zum Laufzeitverhalten sind daher aus dem Code
abgeleitet und als solche gekennzeichnet.

Legende: ✅ vorhanden und nach Code plausibel funktionsfähig · ⚠️ vorhanden, aber unvollständig oder fehlerhaft ·
❌ nur Platzhalter oder simuliert · 🔒 Sicherheitsbefund

---

## 1. Gesamteindruck

Estrich Web ist **fachlich weit entwickelt und technisch am Ende seiner Tragfähigkeit.**

**Fachlich stark:**
- Die Bauakte mit Räumen, Estrichaufbau und Erstbegehung bildet die Arbeit eines Estrichbetriebs realistisch ab.
- Kolonnen- und Wochenplanung, Tageseinteilung, Notizen im Ticket-Stil, Fotodokumentation, Material und Anrufe sind vorhanden.
- In 66 Iterationsrunden sind viele Detailentscheidungen aus dem Betriebsalltag entstanden, etwa Hinweiszeilen
  in der Bauakte, das Anheften von Notizen und die Stornierung mit Passwortbestätigung. Dieses Wissen ist das
  eigentliche Kapital des Projekts.

**Technisch:** Die Anwendung ist auf einen Shared-Webspace (STRATO) zugeschnitten: PHP ohne Framework, MySQL,
`.htaccess`. Sie besteht aus einer einzigen Router-Datei mit rund 3.000 Zeilen, einem globalen Stylesheet mit rund
12.800 Zeilen und mehreren 1.000- bis 3.000-zeiligen Angular-Komponenten. Es gibt keine automatisierten Tests,
keine echte Migrationsverwaltung, Schemaänderungen zur Laufzeit und mehrere Sicherheitslücken.

Die Arbeitsweise ist in den `README_AENDERUNGEN_*.md` dokumentiert: Hotfix-Runden ohne lauffähige PHP-Umgebung
(„im Container steht kein PHP zur Verfügung“) und ohne Produktionsbuild („konnte ich keinen `npm run build`
ausführen“). Das erklärt die Häufung von Folgefehlern, etwa CSS-Klassenkonflikte (`README_AENDERUNGEN_2026-08-22.md`)
oder Anzeigebedingungen, die nach dem Verschieben eines Bausteins nicht mehr stimmten (`README_AENDERUNGEN_2026-08-21.md`).

## 2. Technologie-Stack

| Schicht | Technologie | Version / Umfang |
|---|---|---|
| Frontend | Angular (Standalone Components, Signals, neuer Control Flow `@if/@for`) | 20.3.13 |
| UI-Bibliothek | Angular Material + CDK (Material Design 3) | 20.2.14 |
| Zustand | NgRx Store (nur ein Mini-Reducer, `store/app.reducer.ts`, 20 Zeilen) | 20.1.0 |
| Kalender | FullCalendar (core, daygrid, timegrid, list, interaction) | 6.1.x |
| PDF / Office | jsPDF (PDF im Browser erzeugt), mammoth (DOCX-Vorschau), qrcode | – |
| PWA | `@angular/service-worker` (`ngsw-config.json`) | – |
| Backend | PHP 8.2/8.3 ohne Framework, eigener Mini-Router | `api/index.php` 2.997 Zeilen |
| Datenbank | MySQL 8 / MariaDB 10.11, InnoDB, utf8mb4 | 77 Tabellen im Installationsschema + 19 zur Laufzeit erzeugte |
| Hosting | STRATO Shared Webspace, Apache, `.htaccess` | – |
| Bildverarbeitung | PHP GD (`imagecreatefromstring`, WebP/JPEG) | – |
| E-Mail | eigener SMTP-Client in PHP (`send_smtp_mail`) | – |

**Umfang:** 49 TypeScript-Dateien mit 22.270 Zeilen (davon `module-registry.ts` mit 3.711 Zeilen reine
Konfiguration), 12.788 Zeilen CSS, rund 3.250 Zeilen PHP plus `modules.php` (3.700 Zeilen JSON-Konfiguration),
33 SQL-Dateien sowie 38 Patch- und Hotfix-Dokumente.

## 3. Projektstruktur

```
api/
  index.php            Router, alle Endpunkte, alle Hilfsfunktionen (2.997 Zeilen)
  Core/                Database (PDO), Jwt (HS256), Request, Response, Router – je 14–44 Zeilen
  Services/Audit.php   No-Op (Audit abgeschaltet, siehe 🔒-7)
  config/modules.php   50 generische Module als JSON (Tabellen, Felder, Suchfelder, Rechtebereich)
  tools/purge_deleted.php
database/
  schema.sql, seed.sql, 26 datierte Migrationen, 4 „Komplett“-/„Idempotent“-Skripte
frontend-angular-source/src/app/
  core/                api.service, auth.service, auth.interceptor, auth.guard, module-registry, offline.service
  features/            29 Feature-Ordner (siehe Abschnitt 4)
  shared/              2 Dialoge
install/install.php    Browser-Installer
docs/                  38 Patch-/Hotfix-Notizen (keine Architektur-Dokumentation im engeren Sinn)
```

**Die Modulkonfiguration ist doppelt vorhanden.** `api/config/modules.php` und
`frontend-angular-source/src/app/core/module-registry.ts` enthalten dieselben 50 Module. Die Schlüssel stimmen
überein (geprüft), weitere Unterschiede habe ich nicht untersucht. Jede Feldänderung muss an zwei Stellen
nachgezogen werden.

## 4. Bestehende Funktionen

### 4.1 Fachliche Spezialseiten (eigene Angular-Komponenten)

| Funktion | Komponente (Zeilen) | Status | Bemerkung |
|---|---|---|---|
| Bauakte („Auftragsakte“) mit 10 Reitern: Übersicht, Kontakte, Notizen, Fotos, Videos, Dokumente, Tagesübersicht, Erstbegehung, Räume & Flächen, Material | `order-file` (3.044) | ✅ | Kernstück. Mobile Befehlsleiste, FAB, Hinweiszeilen, Präsenzanzeige („Aktuell in dieser Akte aktiv“) |
| Neue Bauakte (5-Schritt-Assistent: Kunde → Bauakte → Adresse & Ausführung → Räume & Aufbau → Speichern) | `workflow-wizard` (780) | ✅ | für die Baustelle zu lang; offline nur vorgemerkt (siehe ❌-1) |
| Bauaktensuche | `order-search` (172) | ✅ | |
| Erstbegehung (Assistent mit Zähler-/Anmischfotos, Video, „keine Erstbegehung notwendig“) | `initial-inspection-wizard` (452) | ✅ | estrichspezifisch, wertvoll |
| Tagesdokumentation (Wetter per Standort, Fotos) | `daily-documentation` (251) | ✅ | |
| Kolonnen-Wochenplaner | `crew-week-planner` (1.789) | ✅ | |
| Tageseinteilung | `daily-dispatch` (1.104) | ✅ | |
| Arbeitsplan (für alle Mitarbeiter lesbar) | `work-plan` (1.096) | ✅ | |
| Plantafel | `planning-board` (401) | ⚠️ | Route leitet auf den Kolonnenplaner um (`plantafel-board` → `kolonnen-wochenplaner`) |
| Kalender im Google-Stil | `calendar-gcal` (835 + 548 Dialog) | ✅ | Kategorien, Ort, Zuständigkeit |
| Kalender-Board (alt) | `calendar` (626) | ⚠️ | Doppelung mit `calendar-gcal` |
| Anrufe und Anfragen (Status-Stepper, Umwandlung in Bauakte) | `calls` (561 + 265 + 229) | ✅ | |
| Materialbestellungen, Lieferanten | `material-orders` (269 + 299) | ✅ | |
| Stammdaten-Hub | `master-data-hub` (67) | ✅ | |
| Benutzer und Rollen | `admin-management` (567 + 180 + 96) | ✅ | Rollenmatrix mit Bereichsschaltern |
| Firmendaten und Optionen, E-Mail (SMTP) | `app-settings` (197) | ✅ | |
| Profil, Passwortänderung, PIN-Schnellanmeldung | `profile`, `force-password-change` | ✅ | |
| Lexware-Office-Integration (Kunden-/Projekt-Sync, Angebote verknüpfen, Auto-Sync) | `lexware-integration` (200) + rund 130 PHP-Fundstellen | ✅ | außerhalb des künftigen Kernumfangs (siehe 02) |
| Kundenportal (Token + PIN, DIN-5008-Zugangsbrief mit QR-Code) | `customer-portal*` | ⚠️ | per Konstante `PORTAL_ENABLED_DEFAULT = false` abgeschaltet, Routen leiten auf die Startseite um |
| „Was ist neu“ | `whats-new` | ✅ | statischer Text (`api/content/whats-new.txt`) |
| Tagesübersicht (Dashboard) | `dashboard` (302) | ✅ | |
| Offline-Center | `offline` (20) | ❌ | siehe ❌-1 |
| Security Center | `security` (25) | ❌ | statische Werbetexte („Nicht löschbare Änderungsprotokolle …“); das Audit-Log ist tatsächlich abgeschaltet |
| Baustellenakte (alt) | `sites` (28) | ❌ | Platzhalterkarte, ohne Route im Menü |

### 4.2 Generische Module (`/module/{key}`, Komponente `module-page`)

50 Module laufen über eine generische Tabellen- und Formularseite. Davon sind fachlich genutzt: Kunden,
Ansprechpartner, Lieferanten, Räume, Estricharten, Estrichaufbau, Kolonnen, Kolonnenmitglieder, Fahrzeuge,
Estrichpumpen, Maschinen, Abwesenheiten, Aufgaben, CM-Messungen.

**Nur als Datenbanktabelle mit generischem Formular vorhanden, ohne erkennbaren Arbeitsablauf:**
`project_phases`, `planning_boards`, `planning_conflicts`, `defect_comments`, `document_versions`,
`quality_protocols`, `warranty_cases`, `subcontractor_orders`, `notification_templates`, `workflows`,
`checklist_templates`, `api_tokens`, `offline_sync_jobs`.

Diese Module stammen erkennbar aus einem frühen „Pflichtenheft-Abdeckungs“-Gerüst
(`docs/PFLICHTENHEFT-ABDECKUNG.md`). Sie decken Anforderungen formal ab, ohne dass dahinter ein Ablauf steht.

**Nur im Schema, ohne Funktion:** `ai_jobs`, `export_jobs`, `api_webhooks`, `record_permissions`, `photo_annotations`.

## 5. Datenbank

- **77 Tabellen** in `database/estrichweb_install_schema_2026_06_07.sql`. Weitere **19 Tabellen** legt die API
  zur Laufzeit an (`CREATE TABLE IF NOT EXISTS` in `index.php`), zum Beispiel `quick_logins`, `sso_settings`,
  `material_orders`, `project_notes` und `project_number_sequences`.
- **Primärschlüssel:** `CHAR(32)` mit 32 Hex-Zeichen aus `bin2hex(random_bytes(16))` (`index.php:40`).
  ➜ **Vorteil für die Migration:** Die Werte lassen sich verlustfrei in den PostgreSQL-Typ `uuid` überführen, weil
  PostgreSQL beliebige 128-Bit-Werte akzeptiert. Alle Fremdbeziehungen bleiben ohne Zuordnungstabelle erhalten.
- **0 Fremdschlüssel-Constraints** im gesamten Schema. Die referentielle Integrität hängt allein am PHP-Code.
  Verwaiste Datensätze sind wahrscheinlich und vor der Migration zu prüfen (siehe 18).
- **Mandantenspalte `tenant_id`** in allen Fachtabellen. Für eine selbst gehostete Einzelinstallation ist sie
  überflüssig (siehe 03, Entscheidung E-04).
- **Bauakte und Baustelle sind fachlich doppelt.** `projects` (Bauakte) hat Adresse, Start und Ende.
  `construction_sites` hat nahezu dieselben Felder. Beim Hochladen eines Dokuments ohne Baustelle legt die API
  stillschweigend eine Baustelle als Kopie der Bauakte an (`POST /documents/upload`, `index.php` ab Zeile 2576).
- **Löschen:** Seit der Umstellung auf endgültiges Löschen entfernt `purge_where` Zeilen und Dateien (`index.php:215`).
  Die Spalten `deleted_at` existieren weiter, werden aber uneinheitlich ausgewertet.
- **Migrationen:** Es gibt 26 datierte Patches und 4 Sammelskripte. Diese überschneiden sich, und
  `estrichweb_install_schema_2026_06_07.sql` darf ausdrücklich nur auf leere Datenbanken angewendet werden.
  Zusätzlich führt die API **101 Schemaänderungen zur Laufzeit** aus (`add_column_if_missing`,
  `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE`), teilweise bei jedem Request (`ensure_runtime_columns`,
  `ensure_photo_columns`). **Welcher Stand eine Produktivdatenbank tatsächlich hat, lässt sich aus dem Repository
  allein nicht bestimmen.** Die Migration muss deshalb das reale Schema auslesen (siehe 18).
- **Bauaktennummern** `JJJJ-NNN` werden serverseitig über `project_number_sequences` vergeben (`index.php:138`).
  Das Verfahren ist korrekt und atomar und wird übernommen.

## 6. API

- **113 Routen** in einer Datei. 5 generische CRUD-Routen (`/module/{key}`) decken 50 Tabellen ab, dazu kommen
  Spezialrouten. Die Übersicht in `docs/API-ENDPUNKTE.md` ist veraltet (14 Einträge).
- **Keine Versionierung** (`/api/...` ohne `v1`), keine OpenAPI-Beschreibung, kein einheitliches Fehlerformat:
  Einmal antwortet die API mit `{"error":{"message":...}}`, im Shutdown-Handler mit `{"error":"...","file":...}`.
- **Paginierung** nur über `limit` und `offset` (maximal 1.000), ohne stabile Sortierung für Synchronisation.
- **Keine Idempotenz.** Bricht ein Request mobil ab, erzeugt die Wiederholung einen doppelten Datensatz, weil die
  IDs der Server vergibt.
- **Rechteprüfung uneinheitlich.** Es gibt drei Mechanismen nebeneinander: die Routenangabe im `Router`
  (exakter Rechtecode), `require_permission()` (mit Bereichs-Wildcard und Sonderregeln) und `require_module_read()`
  (Freigabeliste).

## 7. Benutzer, Rollen, Rechte

- Es gibt 14 vorgesehene Rollen (`docs/ROLLEN-UND-RECHTE.md`). Mit `2026_06_05_roles_simplify.sql` wurden Rechte
  zu „ein Schalter = voller Bereichszugriff“ vereinfacht. Jeder Bereich hat dabei sechs Aktionen: read, write,
  delete, export, share, archive.
- `require_permission()` (`index.php:65`) enthält **fest codierte Ausnahmen**:
  - `projects`, `dashboard` und `profile` sind für **alle** Benutzer immer erlaubt, auch schreibend
    (`$alwaysAllowed`, Zeile 83).
  - Die Rolle `kolonnenmitglied` wird über ihren Code erkannt und darf nur `reports`, `photos` und `documents`
    schreiben (Zeile 75).
  - Nur „Führungsrollen“ dürfen löschen, erkannt über eine fest codierte Liste von Rollencodes (`leadership_role_codes()`).
- `require_module_read()` (Zeile 94) gibt die Lesebereiche `customers`, `users`, `vehicles`, `settings` und weitere
  für **jeden angemeldeten Benutzer** frei.

**Folge:** Rollen sind nicht frei konfigurierbar. Das Verhalten hängt an Rollencodes im PHP-Code und nicht an
Rechten. Das widerspricht der Anforderung „konkrete Berechtigungen statt fest codierter Rollen“.

## 8. Dokumente und Fotos

- Fotos werden im Browser komprimiert (`api.service.ts`, `compressImageFile`). Serverseitig entstehen mit GD
  Varianten „main“ und „thumb“. Das Original wird standardmäßig **nicht** gespeichert
  (`storage_strategy: client_compressed_plus_server_thumbnail_no_original_by_default`).
- Die Ablage liegt unter `storage/` im Webroot und ist nur über `.htaccess` gesperrt. Ausgeliefert wird über
  `stream_private_file` mit Rechteprüfung.
- Die Rechteprüfung ist **nur mandantenbezogen**: Jeder mit `projects.read` (also jeder Benutzer, siehe 7) kann
  jedes Foto jeder Bauakte abrufen. Eine Objektprüfung auf Bauaktenebene fehlt.
- Es gibt keine Prüfsumme, keine Duplikaterkennung über den Inhalt und keinen fortsetzbaren Upload. Die
  Dateityp-Prüfung erfolgt nur über die Dateiendung.

## 9. UI-Komponenten und Gestaltung

- Angular Material 3 mit sehr starken Überschreibungen: **12.788 Zeilen globales CSS, 2.549 `!important`,
  227 Media-Queries.** Grundregeln sind mehrfach definiert, zum Beispiel `body { font-family … }` viermal und
  `.mat-icon` dreimal.
- Komponentenstile liegen fast vollständig global statt in den Komponenten. Die in den Änderungsnotizen
  beschriebenen Namenskonflikte (`.wz2-steps`, `.quick-hits`/`.cf-hits`) sind die direkte Folge.
- Es gibt erste Design-Tokens (`--ew-button-radius`, `--ew-card-radius`, `--ew-soft-shadow`), aber kein
  geschlossenes Token-System.
- **Positiv:** Die neueren Runden zeigen gutes UX-Gespür, etwa Hinweise als schmale Zeilen statt Kästen, Status
  mit Punkt zusätzlich zur Farbe, 16-px-Eingaben gegen den iOS-Zoom und begrenzte Zeilenlängen. Diese Regeln
  übernehme ich ins Designsystem (05).

## 10. Sicherheitsbefunde

Sortiert nach Schwere. Details und Gegenmaßnahmen stehen in 17.

| # | Befund | Fundstelle | Schwere |
|---|---|---|---|
| 🔒-1 | **Produktionszugangsdaten im Quellpaket:** DB-Host, Name, Benutzer, Passwort und JWT-Secret in `api/config/local.php`; Host, Name und Benutzer zusätzlich in `docs/GO-LIVE-FINAL-2026-06-28.md` | ZIP, nicht ins Repository übernommen | **kritisch** – DB-Passwort und JWT-Secret rotieren |
| 🔒-2 | **Microsoft-SSO ordnet Benutzer per E-Mail aus einem unsignierten Token zu.** Das ID-Token wird ohne Signaturprüfung decodiert (`jwt_decode_unverified`, Zeile 1593). Die Zuordnung erfolgt über `preferred_username`/`email`, der Standard-Tenant ist `common` (also jeder Microsoft-Tenant). `preferred_username` ist kein unveränderliches und kein verifiziertes Merkmal. | `index.php:1562–1613` | hoch (derzeit im Login-UI entfernt, die Endpunkte bestehen aber weiter) |
| 🔒-3 | **2FA wird nicht geprüft.** Ist `two_factor_enabled=1`, verlangt die API nur, *dass* ein Code mitgeschickt wird, nicht dass er stimmt. | `index.php:1495` | hoch (sofern 2FA genutzt wird) |
| 🔒-4 | **Zu weite Lese- und Schreibrechte:** Jeder Benutzer darf Bauakten schreiben. Jeder Benutzer liest alle Benutzer (Name, E-Mail, Telefon), alle Kunden und alle Einstellungen. | `index.php:83, 94` | hoch |
| 🔒-5 | **Access-Token in der URL** (`?access_token=` für Foto- und Dokumentansichten). Es landet in Server-Logs, im Browserverlauf und in Referrer-Headern. | `index.php:52`, `api.service.ts:411, 429` | mittel |
| 🔒-6 | **Cron-Schlüssel = JWT-Secret als URL-Parameter** (`/public/cron/due-tasks?key=<JWT_SECRET>`). Wer das Server-Log sieht, kann Tokens fälschen. | `index.php:1397` | hoch |
| 🔒-7 | **Audit-Log abgeschaltet** („Speicherersparnis“), obwohl Löschungen endgültig sind und das Security Center „nicht löschbare Protokolle“ verspricht | `Services/Audit.php` | mittel (Nachvollziehbarkeit, DSGVO-Nachweis) |
| 🔒-8 | **Interne Fehlerdetails an den Client** (Meldung, Datei, Zeile) – bewusst eingeführt zur Diagnose | `index.php:1289, 2992` | mittel |
| 🔒-9 | **Tokens im `localStorage`**, Refresh-Token ohne Rotation und ohne Bindung an ein Gerät | `auth.service.ts:70–76`, `index.php:1615` | mittel |
| 🔒-10 | **Service Worker cacht `/api/module/**` und `/api/dashboard` benutzerunabhängig.** Auf einem gemeinsam genutzten Gerät kann ein Folgebenutzer offline Daten des Vorgängers sehen. | `ngsw-config.json` | mittel; für Poolgeräte **hoch** |
| 🔒-11 | `Access-Control-Allow-Origin: *` | `index.php:12` | niedrig (Bearer-Token, keine Cookies) |
| 🔒-12 | SSO-Client-Secret wird mit einem aus dem JWT-Secret abgeleiteten Schlüssel verschlüsselt (Schlüsselwiederverwendung). Bei fehlendem OpenSSL wird nur Base64 gespeichert. | `index.php:649` | niedrig |
| 🔒-13 | PIN-Schnellanmeldung: Die `device_id` liefert der Client selbst. `/auth/quick/status` gibt zu einer bekannten Geräte-ID den Benutzernamen ohne Anmeldung heraus. | `index.php:1502` ff. | niedrig |

**Positiv:**
- Durchgängig Prepared Statements.
- Bezeichner werden über `safe_identifier` geprüft.
- Konstante Antwortzeit bei unbekannten Konten.
- Sperre nach Fehlversuchen je Kennung und je IP.
- Refresh-Tokens werden nur als Hash gespeichert.
- `password_hash` (bcrypt) für Passwörter.
- Sicherheitsheader und Rechteprüfung beim Dateizugriff (auf Mandantenebene).

## 11. Funktionen, die nur scheinbar existieren (❌)

| # | Funktion | Tatsächliches Verhalten |
|---|---|---|
| ❌-1 | **Offline-Synchronisation** | `OfflineService` legt fehlgeschlagene Vorgänge im `localStorage` ab. `POST /offline/sync` schreibt sie nur in `offline_sync_jobs` mit Status `queued` (`index.php:2823`). **Kein Code verarbeitet diese Tabelle.** Der Client löscht seine Warteschlange danach (`offline.service.ts`, `sync()` → `clear()`). **Offline erfasste Bauakten gehen dadurch faktisch verloren.** Fotos werden gar nicht offline gepuffert. |
| ❌-2 | Security Center | statische Texte |
| ❌-3 | Audit-Log | No-Op |
| ❌-4 | 2FA | siehe 🔒-3 |
| ❌-5 | KI-Jobs, Export-Jobs, Webhooks, API-Tokens, Datensatzrechte | nur Tabellen |
| ❌-6 | Gerätesitzungen | `device_sessions` wird bei jedem Login neu befüllt und bei jedem Request aktualisiert, aber nie ausgewertet: kein Widerruf, keine Anzeige mit Wirkung |

## 12. Technische Schulden (Auswahl)

1. **Monolithische Dateien:** `index.php` (2.997 Zeilen, alle Fachlogik), `order-file.component.ts` (3.044),
   `crew-week-planner` (1.789), `styles.css` (12.788).
2. **Schemaänderungen zur Laufzeit** statt Migrationen (101 Stellen).
3. **Doppelte Modulkonfiguration** PHP/TS.
4. **Doppelte Entitäten:** Bauakte/Baustelle, Kalender/Kalender-Board, Plantafel/Kolonnenplaner.
5. **Kein Build in der Entwicklungsumgebung:** Laut `RECONSTRUCTED_SOURCE_2026_06_07.md` wurde der
   Produktionsbuild nie im Entwicklungscontainer erzeugt. Die Quelle selbst ist „rekonstruiert“.
6. **PDF-Erzeugung im Browser** (jsPDF in 10 Komponenten). Dadurch sind Layouts uneinheitlich, und die Ergebnisse
   sind nicht serverseitig reproduzierbar.
7. **Webspace-Grenzen:** keine dauerhaften Hintergrundprozesse, Cron nur per HTTP-Aufruf, keine Backups außer den
   STRATO-Sicherungen.

## 13. Testabdeckung

**Keine.** Es gibt keine `*.spec.ts`, kein PHPUnit, keine End-to-End-Tests und keine CI. Die Qualitätssicherung
bestand aus manuellen Rückmeldungen des Betriebs (siehe Änderungsnotizen: „Bitte einmal erneut versuchen“).

## 14. Was übernommen werden kann

| Übernehmen | Art |
|---|---|
| Fachliches Modell: Bauakte, Räume, Estrichaufbau, Estricharten, Erstbegehung, CM-Messung, Kolonnen, Ressourcen, Materialbestellungen, Anrufe/Anfragen | Datenmodell und Abläufe als Spezifikation |
| Alle Bestandsdaten (IDs verlustfrei, siehe 5) | Datenmigration |
| Bauaktennummern-Logik `JJJJ-NNN` | Algorithmus |
| UX-Entscheidungen aus den Änderungsnotizen (Hinweiszeilen, Notizen anheften, Stornierung mit Bestätigung, Raumraster) | Gestaltungsregeln |
| Angular als Web-Framework, ausgewählte Komponentenlogik (Wochenplaner, Kalenderintegration) | Code als Vorlage beim Neuaufbau |
| Login-Sperrlogik, konstante Antwortzeit, Refresh-Token-Hashing | Konzept |

## 15. Was neu entwickelt werden sollte

- **Backend vollständig.** Gründe: PostgreSQL und Docker sind Pflicht, die Synchronisation ist neu, die
  Rechteprüfung muss strukturell anders aufgebaut werden, und der Code ist ohne Tests nicht sicher umbaubar.
- **Web-Oberfläche als neues Angular-Projekt.** Die Fachabläufe des Altbestands dienen als Vorlage. Das
  Stylesheet wird **nicht** übernommen.
- Offline-Synchronisation, Geräteverwaltung, Audit, Backup: Hier existiert nichts Verwendbares.
- Native Apps (iOS, Android, Windows): gibt es bisher nicht.

Begründung und Abwägung stehen in [04-framework-bewertung.md](04-framework-bewertung.md).
