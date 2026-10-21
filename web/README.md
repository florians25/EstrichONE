# EstrichONE Web

Die Büro-Oberfläche von EstrichONE: **OpenUI5 1.136 (LTS)** in TypeScript, gebaut mit dem UI5 Tooling
(`ui5-tooling-transpile`, `ui5-tooling-modules`). Alle OpenUI5-Themes (Horizon und Quartz, jeweils hell, dunkel und
Hochkontrast) sind selbst gehostet und in den Einstellungen wählbar (live, gespeichert, „Systemeinstellung folgen“).
Deutsch, kompakte Dichte, deutsche Zahlen- und Datumsformate. Als PWA installierbar (`public/estrichone.webmanifest`,
kein Service Worker).

**Funktionen:** Anmeldung (HttpOnly-Cookies, CSRF), Ersteinrichtung, Pflicht-Passwortwechsel, Übersicht mit
Kennzahlen und „Warten auf Genehmigung“, Tagesübersicht, Baustellenkarte, Bauakten (Liste → Objektseite → Datei
im Flexible Column Layout) mit Kopf, Kennzahlen und Statusablauf, Anlage als Assistent (Kunde Pflicht,
Dublettenprüfung auf dem Server, Vorlagen), Erstbegehung als Assistent, Dokumente (Ordner, Mehrfach-Upload per
Ziehen, Fassungen, Vorschau, ZIP, Freigaben), Fotos und Videos (Zeitstrahl, Großansicht, Zuordnung), Dokumentation
(Bautagebuch, Prüfungen, Berichte), Räume, Aufgaben/Mängel, Material, Einsätze, Kontakte, Notizen mit
Textbausteinen und Verlauf mit Feldänderungen, Bauakte als PDF (Server). Zentrale Dokumentverwaltung mit
Freigaben. Kolonnenplanung als Plantafel (Kolonnen oder Mitarbeiter, Tag/Woche/Monat, Ziehen, Abwesenheiten,
Konflikte, Vorrat, PDF), Arbeitsplan, Kolonnen/Geräte/Abwesenheiten. Anfragen, Anrufe, Kunden, Material,
Lieferanten, Bestellungen. Benutzer, Rollen und Rechtematrix, Stammdaten (Textbausteine, Ordnervorlagen,
Bauakten-Vorlagen), Geräte mit QR-Einrichtung, Helpdesk, Sicherung, Firmendaten, Protokoll.
Systemhinweise der Hosting-Konsole als Banner, Wartungsseite bei Wartung (503 `MAINTENANCE`).

Seit Runde 3: Hochladen in 4-MB-Teilen mit Wiederholung und Fortsetzen (auch hinter Proxys mit Größengrenze),
gleitende Sitzung mit Anmeldedialog ohne Datenverlust, Hochladen und Vorschau im Dialog (mit Aktenkopf), Kontakte und
Beteiligte, „Letzte Aktivitäten“, „Mir zuweisen“, digitales Abnahmeprotokoll mit Unterschriften und Kundenfreigabe
per Link (öffentliche Seite `/abnahme/:token`), Systemeinstellungen (Firmenlogo, Nummernkreis, Status-Automatik,
Freigaben, Sitzungen, Karte, Darstellung, PDF-Layout, Module), Zugangsbrief nach DIN 5008, alle Ausdrucke als
Server-PDF und automatisch in der Bauakte abgelegt, Dokumentenbaum Kunde → Bauakte → Dokumentart.

## Aufbau

```
webapp/index.html        Einstieg (UI5-Bootstrap, CSP-tauglich ohne Inline-Skript), boot.ts setzt Theme und Dichte
webapp/Component.ts      Komponente, Modelle (device, session)
webapp/controller/       Controller je Seite (BaseController mit Hilfen), Unterordner wie die Views
webapp/view/             XML-Views; bauakte/sections/ sind die Abschnitte der Objektseite
webapp/control/          Eigene Controls: Plantafel, Karte (Leaflet), Upload, Farbfeld
webapp/util/             API-Client, Router (History-API, Deep-Links), Formulare, Assistenten, Themes, Formate …
webapp/i18n/             Texte
public/                  Logos, Favicons, PWA-Manifest (werden unverändert ausgeliefert)
scripts/package.mjs      verpackt den UI5-Build mit versionierten Pfaden (/ui-<hash>/…) für dauerhaftes Caching
```

## Entwickeln

Node.js 24 (bzw. ≥ 22.12) und eine laufende API auf `http://localhost:5080` (siehe `backend/README.md`).

```bash
npm ci
npm start          # http://localhost:4200, /api wird an die API weitergeleitet (ui5.yaml)
npm run lint       # Typprüfung (tsc)
npm run build      # UI5-Build nach build/, verpackt nach dist/
```

## Tests

```bash
npm test                         # Unit-Tests (Vitest)
scripts/e2e.sh                   # Ende zu Ende: PostgreSQL, API, ui5 serve und Playwright (braucht Docker und .NET)
WEB_IMAGE=estrichone/web:dev scripts/e2e.sh   # gegen das fertige Image (Caddy, HTTPS, CSP)
SCREENSHOTS=1 scripts/e2e.sh     # zusätzlich Bildschirmfotos nach test-results/screenshots/
```

## Sicherheit

- Keine Tokens im JavaScript: Die API setzt HttpOnly-, Secure-, SameSite=Strict-Cookies. Der Client sendet den
  CSRF-Token aus dem Cookie `XSRF-TOKEN` im Header `X-XSRF-TOKEN`.
- Content-Security-Policy im Caddyfile unverändert streng: nur eigene Skripte (`script-src 'self'`), kein CDN.
  OpenUI5 wird asynchron ohne Inline-Skript geladen; Farben in eigenen Controls setzt die Oberfläche über CSSOM.
- Die Oberfläche blendet Seiten und Aktionen ohne Recht aus; geprüft wird immer auf dem Server.

## Betrieb

`docker/web.Dockerfile` baut die Anwendung und liefert sie mit Caddy aus (Image `estrichone/web`). Caddy
leitet `/api` an die API weiter und übernimmt TLS (ADR 0007).
