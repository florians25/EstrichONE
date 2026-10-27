# Datensicherung, Wiederherstellung, Archiv und Wartung

EstrichONE sichert sich selbst: täglich mindestens eine **Vollsicherung** (zusätzlich inkrementell möglich), verschlüsselt
mit [restic](https://restic.net) (AES-256, Prüfsummen über jeden Block), auf ein oder mehrere Ziele. Alles Nötige steht im
**Backup Center** der Weboberfläche (System → Sicherung, Recht `backup.manage`). Fällt der Backup-Server aus, sehen alle
Benutzer in Web und Apps ein rotes Banner; Administratoren bekommen nach jeder Sicherung eine Pushnachricht.

## Status-Banner und Wartung für alle Clients (Web-Shell, iOS, Android)

### `GET /api/v1/system/health-banner` (angemeldet, jedes Recht)

Zwischengespeichert bis 10 Minuten (der Server prüft jede Minute selbst) – Clients fragen alle 5 Minuten und nach dem
Start bzw. Wiederverbinden. Auch während einer Wartung erreichbar.

```json
{
  "backup":  { "configured": true, "reachable": false, "lastSuccessAt": "2026-10-26T21:30:12Z", "severity": "critical",
               "message": "ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup 26.10.2026 22:30 Uhr). Bitte geben Sie umgehend Ihrem Admin Bescheid.",
               "restoreRunning": false },
  "archive": { "configured": true, "reachable": false, "lastSuccessAt": "2026-10-27T08:02:00Z", "severity": "warning",
               "message": "Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung 27.10.2026 09:02 Uhr). Bitte informieren Sie Ihren Admin." },
  "banners": [
    { "key": "backup",  "severity": "critical", "text": "ACHTUNG: Backup-Server ausgefallen …" },
    { "key": "archive", "severity": "warning",  "text": "Hinweis: Archiv-Server ausgefallen …" }
  ],
  "checkedAt": "2026-10-27T21:31:00Z"
}
```

Clients zeigen **genau `banners`** an (Reihenfolge = Wichtigkeit, `critical` rot, `warning` gelb, Text unverändert);
leeres Array = kein Banner. Regeln auf dem Server:

| Banner | Bedingung |
|---|---|
| rot `backup` | Backup **eingerichtet** (gekoppeltes Backup-Ziel bzw. Ziel, das zieht, oder mindestens eine erfolgreiche Sicherung) **und** (ein Ziel nicht erreichbar, letzter Lauf fehlgeschlagen oder letzte erfolgreiche Sicherung älter als 26 Stunden). Nicht während einer Wiederherstellung. |
| gelb `archive` | Archivserver gekoppelt **und** nicht erreichbar. |

Zusätzlich benachrichtigt der Server alle Benutzer mit Recht `backup.manage` (Postfach + Push, Kategorie `system`):
beim Ausfall einmal, danach einmal täglich als Erinnerung, und „wieder in Ordnung“, sobald es behoben ist.

### Wartungsmodus sperrt alle Clients (B7)

Während einer Wartung (Systemhinweise, `docs/reengineering/…`, Einstellungen → Systemhinweise) beantwortet die API
**jeden** fachlichen Aufruf von Nicht-Administratoren – auch Sync, Uploads, Planung – mit

```http
HTTP/1.1 503 Service Unavailable
Retry-After: 1800
Content-Type: application/problem+json

{ "title": "Wartungsarbeiten", "detail": "EstrichONE wird auf Version 0.1.0 aktualisiert.", "status": 503,
  "code": "MAINTENANCE", "reason": "maintenance", "startsAt": "2026-10-28T18:00:00Z", "endsAt": "2026-10-28T18:30:00Z",
  "adminsMayWork": true }
```

- `reason` = `maintenance` (geplante Wartung) oder `restore` (Wiederherstellung aus dem Backup Center – dann sind auch
  Administratoren gesperrt, `endsAt` ist `null`).
- Erreichbar bleiben: `/server-info`, `/auth/*`, `/setup`, `/system/notices`, `/system/health-banner`.
- **Apps (iOS/Android):** bei `503` + `code = MAINTENANCE` Sperrbildschirm mit `title`, `detail`, `endsAt`
  („voraussichtlich bis 18:30 Uhr“); **Outbox nicht verwerfen** – der Server hat nichts übernommen, alles wird nach dem
  Ende erneut gesendet. Freigabe erkennen: `GET /system/notices` (ohne Anmeldung) alle 60 Sekunden bzw. nach
  `Retry-After`; `maintenance` fehlt oder `active = false` → entsperren und Sync anstoßen. Nach einer Wiederherstellung
  ändert sich `instanceEpoch` (`/server-info`) – dann vollständig neu abgleichen (docs/sync-protokoll.md).

### Fallback bei Serverausfall (B8)

Ist die API nicht erreichbar (Neustart, Absturz, Update), liefert Caddy für Seitenaufrufe statt einer Fehlermeldung die
Störungsseite (`/stoerung.html`: Logo, „EstrichONE ist gerade nicht erreichbar“, automatischer Neuversuch alle 30 s);
API-Aufrufe bekommen weiterhin `502/503/504` mit `application/problem+json` und `code = SERVER_UNAVAILABLE`. Während einer
Wartung zeigt die Weboberfläche die Wartungsseite. Der Service Worker der PWA zeigt ohne jede Verbindung die
Offline-Seite (`/stoerung.html` ist vorab zwischengespeichert).
