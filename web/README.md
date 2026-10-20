# EstrichONE Web

React 18 mit [Ant Design 5](https://ant.design/) und Ant Design Pro Components (ProLayout, ProTable, Formulare),
gebaut mit Vite. Deutsch als Oberflächensprache, Farben der Marke im Theme (`src/theme.ts`, Navy `#0B2D5B`).
Serverdaten über TanStack Query.

**Stand:** Anmeldung (HttpOnly-Cookies, CSRF), Ersteinrichtung, Pflicht-Passwortwechsel, Übersicht mit Kennzahlen,
Anfragen und Bauakten (anlegen, bearbeiten mit Versionsprüfung, Räume und Flächen, Aufgaben und Mängel, Notizen,
Fotos und Dokumente hochladen, Prüfungen und Bautagebuch, Verlauf), Kunden, Plantafel mit Konflikthinweisen,
Kolonnen/Fahrzeuge/Abwesenheiten, Benutzer mit Einmalpasswort, Geräte mit QR-Code-Einrichtung, Helpdesk,
Sicherungsstatus.

## Aufbau

```
src/api/        Fetch-Client (Cookies, CSRF, einmaliges Erneuern der Sitzung) und DTO-Typen
src/auth/       Anmeldezustand und Rechte
src/layout/     Programmrahmen (ProLayout): Kopfzeile, gruppierte Navigation, Statuszeile
src/pages/      Seiten, je Seite ein Modul (lazy geladen); bauakte/ enthält die Reiter der Bauakte
src/shared/     Status-Tags, Kundenauswahl, Datumshilfen, UUIDv7
```

## Entwickeln

Node.js 24 (bzw. ≥ 22.12) und eine laufende API auf `http://localhost:5080` (siehe `backend/README.md`).

```bash
npm ci
npm run dev        # http://localhost:4200, /api wird an die API weitergeleitet (vite.config.ts)
npm run build      # Typprüfung und Produktions-Build nach dist/
```

## Tests

```bash
npm test                         # Unit-Tests (Vitest)
scripts/e2e.sh                   # Ende zu Ende: PostgreSQL, API, Vite und Playwright (braucht Docker und .NET)
WEB_IMAGE=estrichone/web:dev scripts/e2e.sh   # gegen das fertige Image (Caddy, HTTPS, CSP)
SCREENSHOTS=1 scripts/e2e.sh     # zusätzlich Bildschirmfotos nach test-results/screenshots/
```

## Sicherheit

- Keine Tokens im JavaScript: Die API setzt HttpOnly-, Secure-, SameSite=Strict-Cookies. Der Client sendet den
  CSRF-Token aus dem Cookie `XSRF-TOKEN` im Header `X-XSRF-TOKEN`.
- Content-Security-Policy im Caddyfile: nur eigene Skripte, keine Inline-Skripte. Inline-Styles sind erlaubt,
  weil Ant Design seine Styles zur Laufzeit erzeugt.
- Die Oberfläche blendet Seiten ohne Recht aus; geprüft wird immer auf dem Server.

## Betrieb

`docker/web.Dockerfile` baut die Anwendung und liefert sie mit Caddy aus (Image `estrichone/web`). Caddy
leitet `/api` an die API weiter und übernimmt TLS (ADR 0007).
