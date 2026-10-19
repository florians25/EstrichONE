# Architektur

## Schichten

1. PWA Frontend: `public/index.html`, `assets/app.js`, `service-worker.js`
2. REST API: `api/index.php`
3. Core Services: Request, Response, Router, JWT, Database, Audit
4. Datenbank: MySQL/MariaDB mit Tenant-ID in allen fachlichen Tabellen
5. Private Ablage: `storage/documents`, `storage/photos`, `storage/logs`

## Mandantentrennung

Alle fachlichen Tabellen enthalten `tenant_id`. Jede API-Abfrage filtert nach `tenant_id` des angemeldeten Benutzers. Dadurch kann ein Benutzer niemals Daten eines anderen Mandanten lesen, solange alle neuen Endpunkte diese Regel übernehmen.

## Rollen/Rechte

Rechte sind stringbasiert, zum Beispiel:

- `sites.read`
- `sites.write`
- `planning.read`
- `planning.write`
- `documents.write`
- `audit.read`
- `*`

Die Router-Definition erzwingt erforderliche Rechte pro Route.

## Audit Log

Schreibende Kernoperationen schreiben `audit_logs` mit Benutzer, Mandant, Entität, Aktion, vorherigem und neuem JSON-Zustand, IP und User-Agent.

## Offlinefähigkeit

Die PWA cached statische Assets. Schreibende Aktionen werden bei Verbindungsfehlern in `localStorage` gepuffert und bei erneutem Online-Status synchronisiert.

## Grenzen klassischer Webspaces

- Keine dauerhaften Worker
- Keine WebSockets als eigener Server
- Keine lokale OCR/KI-Verarbeitung im Hintergrund
- Cronjobs nur, falls im Hostingpaket verfügbar

Für diese Funktionen sind externe APIs oder STRATO VPS/Managed Server sinnvoll.
