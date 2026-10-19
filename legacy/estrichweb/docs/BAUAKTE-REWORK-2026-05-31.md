# Bauakte Überarbeitung (2026-05-31)

## Behobener Fehler: HTTP 500 auf `/api/projects/{id}/file`
Der Endpunkt liest die Tabelle `quality_checks`, die im Basis-Schema fehlte.
Sobald eine Bauakte mindestens eine Baustelle hatte (auch automatisch erzeugte),
schlug die Abfrage fehl und die komplette Akte lud nicht (`500 Internal Server Error`).

Behoben durch:
- Neue Hilfsfunktion `safe_query()` – jede Zusatzabfrage der Akte (Dokumente, Fotos,
  QM, Aufgaben, Freigaben) degradiert bei fehlender Tabelle/Spalte zu einer leeren
  Liste, statt die ganze Akte abstürzen zu lassen.
- Neue Funktion `ensure_quality_checks_table()` – legt die Tabelle bei Bedarf an.
- `quality_checks` in `database/schema.sql` ergänzt
  + Migration `database/2026_05_31_quality_checks_bauakte_fix.sql`.

## Bauaktenansicht neu gedacht (Frontend, Angular Material)
Datei: `frontend-angular-source/src/app/features/order-file/order-file.component.ts`

- **Erster Block entfernt:** Die große Hero-Kachel (Kunde/Adresse/Dämmung/Fläche +
  Kennzahlen + Baustellenbild) wurde entfernt. Diese Daten stehen weiterhin im Tab
  „Baustellenübersicht“.
- **Kein Stepper mehr:** Der `mat-horizontal-stepper` wurde entfernt. Der Status wird
  jetzt **oben rechts** über einen Material-Status-Button mit `mat-menu` gesetzt
  (Neu / Geplant / In Arbeit / Abgeschlossen), farblich gekennzeichnet.
- **Keine Fremd-Akten mehr wählbar:** Die Auswahl-/Suchleiste zum Öffnen anderer
  Aufträge wurde aus der Akte entfernt. Die Akte wird über `?project_id=` geöffnet;
  oben links steht nur noch die Identität der aktuellen Akte (Nummer, Kunde, Adresse).
- **Neue Tagesübersicht:** Der frühere Tab „Tagesberichte“ ist jetzt „Tagesübersicht“ –
  eine nach Kalendertagen gruppierte Chronik mit Foto-/Video-/Dokument-Zählern je Tag,
  aktivem Hervorheben des gewählten Eintrags und direktem Viewer.
- Bleibt vollständig im Angular-Material-Konzept (`mat-card`, `mat-menu`, `mat-tab-group`,
  `mat-button`, `mat-icon`).

Styles: ergänzt in `frontend-angular-source/src/styles.css` (Abschnitt „Bauakte Re-Design“).

## Hinweis Build
Das Frontend ist Quellstand. Für das Deployment muss der Angular-Build erzeugt und der
Ordner `/public` auf STRATO ersetzt werden (`npm ci && npm run build`).
Backend sofort einsetzbar: `/api` und `/database` ersetzen.

## Offen / noch zu klären
- „Optimierungen an der Lexware-Schnittstelle“: bitte konkretisieren, was optimiert
  werden soll (z. B. Fehlertoleranz beim Auto-Sync, Mapping-Felder, Beleg-Trigger,
  Performance), damit die bestehende, funktionierende Integration nicht unnötig
  umgebaut wird.

---

## Nachtrag (Redesign-Runde 2)

### Behobener Fehler: Fotos & Dokumente wurden nicht angezeigt
Das Frontend ruft `/api/documents/{id}/view` und `/api/photos/{id}/view` auf
(mit `?access_token=` für `<img>`/`<iframe>`/`<video>`), aber diese Routen
fehlten komplett in der API. Ergebnis: Bilder/Dokumente luden nie.
- Beide Endpunkte ergänzt (streamen die Datei inline, Auth via Query-Token).
- Neue Helper `mime_from_path()`; `quality_checks`/`safe_query` wie gehabt.
- Foto-Varianten: `main` (komprimiert), `thumb`, `original`.

### Bauakte – Re-Design Runde 2
- **Historie entfernt** (Übersicht).
- **Keine `0000-00-00`-Datumswerte mehr** – neuer `fmtDate()` zeigt `—`.
- **Echte Foto-Galerie:** Übersicht zeigt Objektbilder als Hero + Thumbnails;
  Tab „Dateianlagen" zeigt alle Bilder als Thumbnail-Raster.
- **Lightbox:** Klick auf ein Foto öffnet eine Vollbild-Ansicht (auch mobil).
- **Status-Pille** in den Baustellendaten.
- **Mobil stark überarbeitet:** Header stapelt sauberer, Aktionen/Status als
  volle Touch-Buttons, Tabs scrollbar mit Sticky-Header, Galerie-Raster und
  KPIs brechen sauber um, Viewer/Karte behalten Mindesthöhe.
- Referenzen geprüft (PlanRadar, Capmo, BauMaster, 123erfasst u. a.):
  strukturierte Datenkarten, Foto-Doku als Galerie, Dokumenten-Repository,
  Status/Aufgaben – diese Muster sind jetzt umgesetzt.

### Build
`npm ci && npm run build` läuft sauber durch (Angular 20, production).
Hinweis: In `package-lock.json` zeigen die Tarball-URLs jetzt auf
`registry.npmjs.org` (öffentliche Quelle).

---

## Runde 3 (App-Shell, Dashboard, Kunden, Kolonnen)

- **Branding:** Anwendung heißt wieder durchgängig **EstrichWeb** (Sidebar, Topbar,
  Login, Seitentitel, index.html).
- **Oben rechts: Benutzer-/Kontomenü** – Avatar mit Initialen, Name, E-Mail,
  Mandant, Links zu Stammdaten/Kunden/Benutzer & Rollen/Sicherheit, Abmelden.
- **Sidebar überarbeitet:** Marken-Logo mit Verlauf, neue Gruppe „Ressourcen“
  (Kolonnen, Mitarbeiter, **Fahrzeuge**, Maschinen), Kunden unter „Büro“,
  aktive Einträge hervorgehoben, Benutzer-Karte im Footer.
- **Fahrzeugverwaltung** als Menüpunkt (Ressourcen) + Dashboard-Kachel.
- **Dashboard / Übersicht komplett neu:** Begrüßungs-Hero, farbige KPI-Karten
  (klickbar), „Heutige Einsätze“ und „Schnellzugriff“, voll responsiv.
- **Bauakte – Abschließen:** grüner Button „Abschließen“ mit Bestätigungsdialog,
  setzt Status auf „Abgeschlossen“ (nur sichtbar, solange nicht abgeschlossen).
  Status-Anzeige oben rechts optisch aufgewertet.
- **Dokumente & Bilder korrekt zugeordnet:** In der Tagesdokumentation wurden
  Fotos/Videos bisher mit dem rohen (oft „__auto__“) Wert gespeichert und landeten
  auf einer Pseudo-Baustelle. Jetzt wird die aufgelöste Baustelle der Bauakte
  verwendet – Medien erscheinen nur noch in der zugehörigen Bauakte.
- **Kundenverwaltung:** Hinweisbox „Kunden werden über Lexware verwaltet“,
  Button **„Lexware aktualisieren“** (Sync), und die manuelle Anlage ist als
  **„Notfall-Anlage“** gekennzeichnet.
- **Kolonnenplan:** Streifen „Letzte Aufträge“ entfernt, Filter neutral
  „Alle Bauakten“. (Hinweis: ein tiefergehender Umbau der Planungslogik braucht
  noch konkrete Vorgaben – siehe offene Punkte.)

### Offen / noch zu klären
- Kolonnenplan „viel besser“: bitte konkretisieren (z. B. Mehrfachzuweisung,
  Kapazitäts-/Konfliktanzeige, Material/Fahrzeug je Einsatz, Monatsansicht).
- Lexware-Schnittstellen-Optimierungen weiterhin offen.

---

## Runde 4 (UI-Politur, Dropdowns, Rollen)

- **Dashboard lädt wieder:** „/dashboard“ war anfällig (zählte u. a. `defects`,
  `quality_protocols`, alle Modul-Tabellen). Jetzt defensiv – fehlt eine Tabelle,
  zählt sie 0 statt 500. Behebt das hängende „Übersicht wird geladen…“.
- **Modal-Politur:** Titel/Labels wurden oben abgeschnitten – der MDC-`::before`-
  Baseline-Platzhalter wird entfernt, Titel/Content bekommen sauberes Padding.
  **Icons in Modals** sitzen jetzt in einem dezenten Chip mit Abstand zum Text.
  **Abstand zwischen Inhalt und Buttons** in Dialogen vergrößert (Trennlinie +
  Padding).
- **Dropdowns statt Freitext bei IDs:** ID-Felder (Kunde, Kolonne, Vorarbeiter,
  Benutzer, Fahrzeug, Baustelle, Maschine, Raum, Rolle …) werden in den
  Modul-Formularen als **Auswahl-Dropdown** mit echten Datensätzen gerendert,
  Label ohne „-ID“-Suffix.
- **Deutsche Dropdown-Bezeichnungen:** Status-Werte vereinheitlicht
  (available→Verfügbar, maintenance→In Wartung, inactive→Inaktiv, active→Aktiv,
  reserved→Reserviert, broken→Defekt …).
- **Rollen & Rechte komplett neu:** Neue Backend-Endpunkte
  (`/admin/roles`, `/admin/permissions`, `POST /admin/roles`,
  `PUT /admin/roles/{id}/permissions`). Neue Oberfläche: Rollenliste +
  **Rechte-Matrix** (Bereiche × Lesen/Bearbeiten/Löschen/Export/Teilen/Archiv)
  mit „Alle“-Schalter pro Bereich, **neue Rolle anlegen**, Speichern je Rolle.
  Benutzer-Rollen-Zuweisung (Mehrfachauswahl) bleibt erhalten.

---

## Runde 5 (Dokumente, Details, Zuweisung, PDF, Kolonnenplan)

- **Dokumente je Bauakte exakt getrennt:** `documents` und `photos` haben jetzt
  eine explizite `project_id` (wird beim Upload/Tagesdoku gesetzt und für
  Altbestände automatisch nachgetragen). Die Akte zeigt ausschließlich ihre
  eigenen Dokumente/Fotos – das Vermischen über Bauakten hinweg ist behoben.
- **Details als Material-Formular + Bearbeiten:** „Baustellendaten" wird als
  Angular-Material-Formularfelder dargestellt (schreibgeschützt), per
  „Bearbeiten" editierbar und speicherbar (Auftrag + Baustelle). Datum-Felder
  inklusive.
- **Dokumente groß öffnen / herunterladen:** Im Viewer gibt es jetzt „Groß
  öffnen" und „Herunterladen" (echter Download via `Content-Disposition:
  attachment`).
- **Zuweisung = Mitarbeiter, nicht zwingend Aufgabe:** „Zuweisen" weist die
  Bauakte einem Mitarbeiter zu (neues Feld „Zugewiesen an"). Eine Aufgabe wird
  nur erstellt, wenn man es ausdrücklich anhakt (Standard: aus).
- **Erstbegehung ausführen ODER ablehnen:** Im Wizard ersetzt „Speichern" zwei
  Aktionen – „Erstbegehung ausführen" (Status abgeschlossen) und „Ablehnen"
  (mit Pflicht-Begründung, Status abgelehnt, High-Prio-Aufgabe).
- **OK-Button hellblau:** Snackbar-Aktion ist jetzt hellblau.
- **Objektbild + Mobil:** In der Akte erscheint ein Objektbild (erstes Foto),
  klickbar/zoombar; Detail-Layout ist mobil einspaltig.
- **Datum durchgängig deutsch:** Deutsche Locale (`de-DE`) registriert –
  Wochentage/Monate/Datumsformate erscheinen deutsch.
- **Kolonnenplan-Fehler behoben:** Eingeplante Kolonnen tauchten in CET/CESZ
  nicht in der richtigen Spalte auf – Ursache war eine UTC-Umrechnung
  (`toISOString`), die lokale Mitternacht einen Tag zurückschob. Datumslogik
  nutzt jetzt lokale Komponenten.
- **Echter PDF-Download:** Bauakte und Kolonnenplan erzeugen jetzt ein echtes
  PDF zum Herunterladen (jsPDF) statt den Druckdialog zu öffnen.
