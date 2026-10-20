# EstrichONE Web

Angular 22 (Standalone-Komponenten, Signals, ohne zone.js), eigenes Designsystem nach
[`docs/reengineering/05-designsystem.md`](../docs/reengineering/05-designsystem.md) statt einer Komponentenbibliothek.

**Stand (AP-14):** Anmeldung (HttpOnly-Cookies, CSRF), Ersteinrichtung, Pflicht-Passwortwechsel, Bauakten ansehen,
Benutzerverwaltung mit Einmalpasswort, Geräte freigeben/sperren/entfernen, Einrichtungscode als QR-Code,
Sicherungsstatus. Bauakten im Büro anlegen und bearbeiten folgt mit AP-18.

## Entwickeln

Node.js 24 (bzw. ≥ 22.22.3) und eine laufende API auf `http://localhost:5080` (siehe `backend/README.md`).

```bash
npm ci
npx ng serve --proxy-config proxy.conf.json   # http://localhost:4200, /api wird an die API weitergeleitet
```

## Tests

```bash
npx ng test --watch=false        # Unit-Tests (Vitest)
scripts/e2e.sh                   # Ende zu Ende: PostgreSQL, API, Web und Playwright (braucht Docker und .NET)
WEB_IMAGE=estrichone/web:dev scripts/e2e.sh   # gegen das fertige Image (Caddy, HTTPS, strikte CSP)
```

## Sicherheit

- Keine Tokens im JavaScript: Die API setzt HttpOnly-, Secure-, SameSite=Strict-Cookies. Angular sendet den
  CSRF-Token aus dem Cookie `XSRF-TOKEN` automatisch im Header `X-XSRF-TOKEN`.
- Strikte Content-Security-Policy im Caddyfile: nur eigene Skripte, keine Inline-Skripte.
- Die Oberfläche blendet Seiten ohne Recht aus; geprüft wird immer auf dem Server.

## Betrieb

`docker/web.Dockerfile` baut die Anwendung und liefert sie mit Caddy aus (Image `estrichone/web`). Caddy
leitet `/api` an die API weiter und übernimmt TLS (ADR 0007).
