# 16 – EstrichONE Backup Center

**Status heute:** keine eigene Sicherung; es gibt nur die STRATO-Webspace-Sicherungen (`docs/STRATO-DEPLOYMENT.md`:
„STRATO-Backups zusätzlich mit eigenen Exporten ergänzen“). Das Update-Skript in den Änderungsnotizen
verlangt „Datensicherung anlegen“, ohne Werkzeug dafür.

**[Vorschlag] Priorität:** Das Backup ist **Voraussetzung für die Inbetriebnahme** des neuen Servers und kommt
in der Roadmap deshalb vor die Apps (20).

## 1. Architektur

```
backup-Container (eigenes Image: restic, postgresql-client 17, Bash, Cron-Ersatz „supercronic“)
  ├─ liest:   postgres (pg_dump über internes Netz, Rolle backup_reader nur mit Leserechten)
  │           /data/files (nur lesend eingebunden)
  │           Data-Protection-Schlüsselring, Konfiguration (ohne .env-Secrets → separat, siehe 4)
  ├─ schreibt: restic-Repository → Ziel A (Pflicht) + Ziel B (optional)
  └─ meldet:  Status per Datei/HTTP an die API (backup_runs) → Backup Center im Web + Benachrichtigung
```

- **Datenbank:** `pg_dump --format=custom` in eine temporäre Datei, dann ins restic-Repository. Bei der
  geplanten Datenmenge eines Betriebs (< 5 GB Datenbank) ist das einfacher und robuster als
  WAL-Archivierung. **[Vormerken]** Point-in-Time-Recovery ist für einen kleinen Betrieb nicht nötig.
- **Dateien:** restic sichert `/data/files` direkt. Die Sicherung ist inkrementell und dedupliziert, sodass
  tägliche Läufe schnell bleiben.
- **Konsistenz:** Zuerst Datenbank-Dump, dann Dateien. Dateien sind unveränderlich (nur anlegen und löschen).
  Ein Foto, das nach dem Dump hochgeladen wurde, ist daher im Backup, aber nicht in der DB. Das Restore-Werkzeug
  meldet solche Waisen und kann sie wieder zuordnen, sofern das Gerät die Metadaten nachliefert (15).
- **Verschlüsselung:** restic verschlüsselt immer (AES-256, Poly1305). Das Repository-Passwort erzeugt die
  Ersteinrichtung. **Es muss außerhalb des Servers aufbewahrt werden.** Das Backup Center erzwingt die Bestätigung
  „Ich habe das Wiederherstellungs-Passwort sicher notiert“ und zeigt das Passwort danach nie wieder an.
- **Nicht in kurzlebigen Containern:** Repositorys liegen auf einem eingebundenen Host-Pfad, einem NAS oder
  einem SFTP-Ziel, nie im Container-Dateisystem.

## 2. Ziele

| Ziel | Umsetzung | Hinweis |
|---|---|---|
| Lokaler Pfad | Host-Verzeichnis, z. B. `/srv/backup/estrichone`, idealerweise **eine zweite Platte** | schützt nicht gegen Diebstahl, Brand oder Ransomware auf dem Host |
| NAS | SMB/NFS auf dem Host eingehängt → als Pfad eingebunden | Anleitung für Synology/QNAP |
| SFTP | restic nativ (`sftp:user@host:/pfad`), Schlüsselauthentifizierung | z. B. ein NAS an einem zweiten Standort |
| S3-kompatibel | restic nativ | **optional**, kein Pflichtdienst |

**Empfehlung im Backup Center (3-2-1):** ein lokales Ziel **und** ein externes Ziel. Fehlt das zweite, zeigt das
Backup Center einen dauerhaften Hinweis an.

## 3. Zeitplan und Aufbewahrung

| Einstellung | Standard |
|---|---|
| Automatisch | täglich 22:30 (außerhalb der Arbeitszeit, konfigurierbar) |
| Manuell | „Jetzt sichern“ (z. B. vor einem Update – das Update-Skript löst es automatisch aus) |
| Aufbewahrung | `restic forget --keep-daily 14 --keep-weekly 8 --keep-monthly 12 --keep-yearly 3` + `prune` wöchentlich |
| Integritätsprüfung | wöchentlich `restic check --read-data-subset=10%`, monatlich vollständig |
| **Wiederherstellungstest** | wöchentlich automatisch (siehe 5) |

## 4. Was gesichert wird

| Inhalt | Im Backup | Bemerkung |
|---|---|---|
| Datenbank | ✅ | |
| Dateien (Fotos, Dokumente, Videos) | ✅ | |
| Data-Protection-Schlüsselring | ✅ | ohne ihn sind gespeicherte Entra-Client-Secrets und SMTP-Passwörter unlesbar |
| Konfiguration (`compose.yml`, `Caddyfile`, Version) | ✅ | |
| `.env` mit Secrets (DB-Passwort, JWT-Schlüssel, restic-Passwort) | **nein** | gehört getrennt verwahrt (Ausdruck/Passwortmanager). Das Restore-Werkzeug fragt die Werte ab. |
| Caddy-Zertifikate | nein | werden neu ausgestellt |

## 5. Wiederherstellung

**[Verbindlich]** Die Wiederherstellung muss möglich sein, wenn Weboberfläche und Datenbank nicht mehr
funktionieren.

**Unabhängiges Werkzeug `estrichone-restore`** (im Backup-Image enthalten; zusätzlich als Anleitung mit
reinen restic- und pg_restore-Befehlen, falls auch das Image fehlt):

```bash
# Neuer, leerer Server: nur Docker installiert
docker run --rm -it -v /srv/estrichone:/srv/estrichone -v /srv/backup:/backup \
  ghcr.io/<herausgeber>/estrichone-backup:<version> restore
# → fragt: Repository (Pfad/SFTP), Passwort, Snapshot (Liste mit Datum/Größe)
# → prüft: Snapshot vollständig? (restic check für diesen Snapshot)
# → stellt her: Dateien → /srv/estrichone/files, Dump → neue Datenbank, Schlüsselring, Konfiguration
# → setzt instanceEpoch neu (Clients gleichen vollständig ab, siehe 15)
# → Bericht: Anzahl Bauakten/Dateien, fehlende Dateien, verwaiste Dateien
```

- **Teilwiederherstellung [1.1]:** einzelne Bauakte oder Dateien aus einem Snapshot in eine Wiederherstellungs-
  Ablage, Übernahme durch den Administrator. Wird erst gebaut, wenn das Vollrestore sicher funktioniert.
- **Wiederherstellungstest (automatisch, wöchentlich):** Der Backup-Container stellt den neuesten Snapshot in
  eine **temporäre** Datenbank `estrichone_restoretest` wieder her (ohne Dateien, nur stichprobenhaft 50 Dateien
  mit Hash-Prüfung). Er führt Plausibilitätsabfragen aus (Zeilenzahlen gegenüber der Live-DB ±1 %, Migrationsstand)
  und löscht die Testdatenbank wieder. Das Ergebnis erscheint im Backup Center. **Eine Sicherung gilt erst als
  „geprüft“, wenn ein Wiederherstellungstest sie bestätigt hat.**

## 6. Backup Center (Web, Recht `backup.manage`)

```
Backup Center
 Zustand  ✓ Letzte Sicherung heute 22:31 (4 min, 312 MB neu)   ✓ Wiederherstellung getestet: So 03:00
          ⚠ Kein externes Sicherungsziel eingerichtet – bei Brand oder Diebstahl wären alle Daten verloren.
 [Jetzt sichern]   [Ziele]   [Zeitplan & Aufbewahrung]   [Protokoll]   [Wiederherstellung – Anleitung]
 Snapshots: Datum · Größe · geprüft · Ziel(e)
```

- **Fehlerbenachrichtigung:** an alle Benutzer mit `backup.manage` (In-App + E-Mail, falls SMTP). Zusätzlich
  meldet `GET /health/ready` „degraded“, wenn die letzte erfolgreiche Sicherung älter als 36 h ist. Damit kann
  externes Monitoring (z. B. Uptime Kuma) alarmieren.
- Eine Wiederherstellung **über die Weboberfläche** wird bewusst **nicht** angeboten. Ein Fehlklick würde die
  Live-Daten überschreiben, und im Ernstfall läuft die Weboberfläche ohnehin nicht.

## 7. Akzeptanzkriterien

- **AK-1:** Auf einem frischen Linux-Server mit Docker gelingt das Restore aus einem SFTP-Repository nach der
  Anleitung ohne Zugriff auf den alten Server. EstrichONE startet danach mit identischen Bauakten,
  Benutzern und Fotos (automatisierter Test in CI mit Testdaten).
- **AK-2:** Ein manipuliertes Repository (gelöschtes Pack-File) wird von der Integritätsprüfung erkannt und gemeldet.
- **AK-3:** Ein fehlgeschlagenes Backup erzeugt innerhalb von 5 min eine Benachrichtigung.
