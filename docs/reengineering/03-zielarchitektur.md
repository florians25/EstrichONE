# 03 – Zielarchitektur

Kennzeichnung: **[Verbindlich]** · **[Vorschlag]** · **[Offen]** (wie in 04).

## 1. Überblick

```
                         ┌──────────────────────────── Firmenserver / VPS (Linux, Docker Compose) ───────────────────────────┐
  Web (Angular) ───┐     │                                                                                                    │
  Desktop (WinUI) ─┤HTTPS│  caddy (Reverse Proxy, TLS)  ──►  api (ASP.NET Core, modularer Monolith)  ──►  postgres           │
  iOS (SwiftUI) ───┤────►│     └─ liefert Web-App aus           ├─ Hintergrunddienste (Sync, Mails,      (nur intern)        │
  Android (Compose)┘     │                                      │   Bildvarianten, Erinnerungen)                             │
                         │                                      └─► /data/files (Volume → Host-Pfad)                         │
                         │  backup (pg_dump + restic, Zeitplan) ──► lokales Ziel / NAS / SFTP                                │
                         └────────────────────────────────────────────────────────────────────────────────────────────────────┘
  Optional, nur wenn konfiguriert:  Microsoft Entra ID (SSO) · SMTP · APNs/FCM (Push) · Wetter-/Geocoding-Anbieter
```

**[Verbindlich]** Clients sprechen ausschließlich mit der API. Es gibt keinen direkten Datenbankzugriff.
**[Vorschlag]** Fünf Container: `caddy`, `api`, `postgres`, `backup` und ein einmaliger `migrate`-Job.
Keine Microservices, kein Message-Broker, kein Redis.

## 2. Modularer Monolith

Ein .NET-Projekt `EstrichOne.Api` mit fachlichen Modulen als Ordner und Namespaces. Jedes Modul enthält
seine Endpunkte, Anwendungslogik und EF-Konfiguration. Module rufen sich nur über öffentliche Dienste
(Interfaces) auf und greifen nie auf fremde Tabellen zu. Ein Architekturtest (NetArchTest) prüft das.

| Modul | Verantwortung | Tabellen (Auszug) |
|---|---|---|
| `Identity` | Benutzer, lokale Anmeldung, Sitzungen, Tokens, Entra-Verknüpfung | `users`, `user_credentials`, `sessions`, `external_identities` |
| `Access` | Rechtekatalog, Rollen, Rollenzuweisung, Objektregeln | `roles`, `role_permissions`, `user_roles` |
| `Devices` | Geräteregistrierung, Freigabe, Richtlinien, Befehle (Fernlöschen) | `devices`, `device_enrollments`, `device_policies`, `device_commands` |
| `Organization` | Firmendaten, Einstellungen, Mitarbeiter, Kolonnen, Ressourcen, Abwesenheiten | `organization`, `settings`, `employees`, `crews`, `crew_members`, `vehicles`, `pumps`, `machines`, `absences` |
| `Contacts` | Kunden, Ansprechpartner, Lieferanten, externe Referenzen | `contacts`, `contact_persons`, `external_refs` |
| `Bauakten` | Bauakte, Bereiche, Räume, Estrichaufbau, Notizen, Hinweise, Nummernkreis | `bauakten`, `bauakte_bereiche`, `rooms`, `screed_buildups`, `notes`, `number_sequences` |
| `Documentation` | Fotos, Dokumente, Videos, Bautagebuch, Prüfungen (Erstbegehung, CM), Abnahmen | `files`, `file_variants`, `diary_entries`, `inspections` |
| `Work` | Aufgaben (inkl. Mängel), Kommentare | `tasks`, `task_comments` |
| `Planning` | Einsätze, Kalender, Kategorien, Konfliktberechnung | `assignments`, `calendar_events`, `calendar_categories` |
| `Office` | Anrufe/Anfragen, Materialbestellungen | `calls`, `material_orders`, `material_order_items` |
| `Helpdesk` | Tickets, Kommentare, Diagnosedaten | `tickets`, `ticket_comments`, `ticket_diagnostics` |
| `Sync` | Operationsannahme, Idempotenz, Änderungsfeed | `sync_operations`, `change_log` |
| `Notifications` | In-App-Benachrichtigungen, E-Mail, Push-Zustellung | `notifications`, `push_registrations` |
| `Audit` | Audit-Log (nur anfügen), Sicherheitsereignisse | `audit_events` |
| `Backup` | Status und Steuerung des Backup-Containers, Protokolle | `backup_runs` |
| `Platform` | Health, Version, Server-Info für das Onboarding, Migrationen | – |

## 3. Datenmodell – Grundsätze

1. **IDs:** `uuid`. Neue IDs erzeugt der **Client** (UUIDv7, zeitlich sortierbar). Damit sind Offline-Anlage
   und idempotente Wiederholung möglich. Altdaten behalten ihre 32-Hex-IDs (siehe 18).
2. **Versionierung:** Jede synchronisierbare Tabelle hat `version bigint` (bei jedem Update +1),
   `updated_at`, `updated_by`, `updated_by_device`.
3. **Löschen:** Synchronisierbare Datensätze erhalten `deleted_at` als **Tombstone**. Die Zeile bleibt
   bestehen, bis alle Geräte den Änderungsfeed über diesen Zeitpunkt hinaus gelesen haben, längstens 90 Tage.
   Danach wird endgültig gelöscht, einschließlich der Dateien. Der Papierkorb für Bauakten ist für berechtigte
   Benutzer 30 Tage wiederherstellbar.
4. **Fremdschlüssel sind Pflicht.** Ihr Fehlen ist im Altbestand die Hauptursache für verwaiste Daten.
5. **Kein `tenant_id`.** **[Vorschlag] E-04:** Eine Installation entspricht einem Betrieb. Die Mandantenspalte
   entfällt. Begründung: weniger Fehlerquellen (jede Abfrage müsste filtern), einfacheres Backup und Restore.
   Wer mehrere Firmen betreibt, betreibt mehrere Installationen.
6. **Zeit:** `timestamptz` in UTC; Anzeige in `Europe/Berlin` (konfigurierbar).
7. **Geld/Fläche:** `numeric` (Fläche in m² mit 2 Nachkommastellen, Dicke in mm als Ganzzahl).

## 4. Kernentitäten (vereinfacht)

```
organization (1 Zeile)                     users ─┬─ user_roles ── roles ── role_permissions
                                                  ├─ external_identities (entra: tid + oid)
contacts (Kunde/Firma/Person) ── contact_persons  └─ sessions ── devices ── device_enrollments
        │
bauakten ─────────────────────┬─ bauakte_bereiche ── rooms ── screed_buildups ── screed_types
  id, nummer (JJJJ-NNN, null   ├─ bauakte_beteiligte (contact_persons mit Rolle: Bauherr, Architekt …)
  bis Sync), bezeichnung,      ├─ bauakte_mitglieder (users mit Zugriff/Zuständigkeit)
  adresse, geo (optional),     ├─ notes
  status, kunde_id (optional), ├─ files (Foto/Video/Dokument) ── file_variants
  vorlage, erstellt_auf_geraet ├─ diary_entries (Bautagebuch)
                               ├─ inspections (typ: erstbegehung | cm_messung | abnahme | pruefung)
                               ├─ tasks (typ: aufgabe | mangel) ── task_comments
                               ├─ assignments (Einsätze: Kolonne, Datum, Fahrzeuge/Pumpen)
                               └─ material_orders ── material_order_items
tickets ── ticket_comments ── (optional) bauakte_id, device_id, user_id
audit_events (append-only)  ·  sync_operations  ·  change_log
```

**Bauakte statt Projekt/Baustelle:** Ein Datensatz trägt Bezeichnung und Baustellenadresse. Der bisherige
Fall „mehrere Baustellen je Projekt“ wird über `bauakte_bereiche` abgebildet. **E-05 entschieden.**

## 5. API-Vertrag

- **Basis:** `/api/v1/…`, JSON, OpenAPI 3.1 in `contracts/openapi.yaml`. Nicht abwärtskompatible Änderungen
  erfordern `/api/v2`. Ein Server unterstützt mindestens die vorherige Hauptversion für 12 Monate.
- **Fehlerformat:** RFC 9457 (`application/problem+json`) mit stabilem `code`, z. B. `BAUAKTE_NOT_FOUND`,
  `PERMISSION_DENIED`, `DEVICE_REVOKED`, `VERSION_CONFLICT`. **Keine internen Details**; stattdessen eine
  `traceId`, die im Server-Log auffindbar ist.
- **Idempotenz:** Schreibende Endpunkte akzeptieren den Header `Idempotency-Key`. Synchronisierte Operationen
  tragen eine `opId`. Wiederholungen liefern das gespeicherte Ergebnis.
- **Optimistische Sperre:** `If-Match: "<version>"` bei Änderungen; bei Abweichung `409 VERSION_CONFLICT` mit
  aktuellem Serverstand.
- **Paginierung:** Cursor-basiert (`?cursor=…&limit=…`), stabile Sortierung.
- **Authentifizierung:** `Authorization: Bearer <access>`. Kein Token in URLs. Dateien werden ebenfalls mit
  Bearer-Token abgerufen (`GET /files/{id}/content`, mit Range für Videos). Signierte URLs sind nicht nötig,
  weil alle Clients Anfragen authentifizieren können (Web: Sitzungs-Cookie). Damit entfällt ein weiterer Schlüssel.

### Endpunktgruppen (Auszug)

| Gruppe | Beispiele |
|---|---|
| Plattform | `GET /api/v1/server-info` (öffentlich: Name, Version, min. Client-Version, Anmeldemethoden), `GET /health/live`, `GET /health/ready` |
| Auth | `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `POST /auth/entra/exchange`, `GET /auth/entra/web/start` |
| Geräte | `POST /devices/enrollments` (Antrag), `POST /devices/enroll-with-code` (QR), `GET /devices/me`, `POST /devices/me/heartbeat` |
| Bauakten | `GET /bauakten?query=&status=&mine=`, `GET /bauakten/{id}`, `PUT /bauakten/{id}`, `GET /bauakten/{id}/hinweise`, `POST /bauakten/duplicate-check` |
| Dateien | `POST /files` (Ankündigung mit SHA-256), `PUT /files/{id}/content` (ganz oder mit `Content-Range` in Teilen), `GET /files/{id}/upload-status`, `GET /files/{id}/content?variant=` |
| Sync | `POST /sync/push`, `GET /sync/pull?cursor=` |
| Admin | `/admin/users`, `/admin/roles`, `/admin/devices`, `/admin/sso`, `/admin/backup`, `/admin/audit` |
| Helpdesk | `/tickets`, `/tickets/{id}/comments`, `/tickets/{id}/diagnostics` |

## 6. Rechteprüfung

1. **Rechtekatalog** im Code (z. B. `bauakten.read`, `bauakten.create`, `bauakten.update`, `bauakten.delete`,
   `bauakten.read_all`, `files.upload`, `planning.manage`, `devices.manage`, `helpdesk.agent`, `backup.manage`,
   `system.admin`). Rollen sind frei konfigurierbare Bündel. Standardrollen werden bei der Ersteinrichtung
   angelegt und können geändert werden.
2. **Objektregel Bauakte:** Mit `bauakten.read_all` sieht man alle Bauakten. Ohne dieses Recht nur Bauakten,
   in denen man Mitglied ist, zuständig ist oder über einen Einsatz der eigenen Kolonne eingeplant ist.
   Dateien, Notizen und Aufgaben erben die Sichtbarkeit der Bauakte.
3. **Durchsetzung:** ASP.NET-Core-Policies je Endpunkt plus eine zentrale Abfragefunktion
   `BauakteAccess.Filter(query, user)`. Diese Funktion verwenden **alle** Listen, die Synchronisation und die
   Dateiauslieferung. Integrationstests prüfen für jede Rolle „darf“ und „darf nicht“.
4. **Keine Logik an Rollencodes.** Die heutigen Sonderfälle (Kolonnenmitglied, Führungskräfte) werden zu
   Rechten in Standardrollen.

## 7. Dateiverwaltung

- Ablage außerhalb der Datenbank unter `/data/files/<jahr>/<monat>/<uuid>`. Die Datenbank speichert Metadaten,
  SHA-256, Größe und MIME-Typ, ermittelt aus dem Inhalt und nicht aus der Endung.
- Upload in zwei Schritten: Ankündigung (`POST /files`, idempotent über die ID, mit Größe und SHA-256 – auch offline
  vorbereitet), dann Inhalt (`PUT /files/{id}/content`). Der Server prüft Größe, Prüfsumme und Typ anhand der
  Magic Bytes und gibt die Datei erst danach frei. Ist sie bereits gespeichert, antwortet er sofort mit 200.
- Große Videos laden in Teilen mit `Content-Range` hoch; `GET /files/{id}/upload-status` liefert die Position zum Fortsetzen.
- Bildvarianten (Vorschau 400 px, Ansicht 2048 px, WebP) erzeugt ein Hintergrunddienst. Das Original bleibt
  **standardmäßig erhalten** (Beweiswert in Streitfällen am Bau); per Einstellung abschaltbar. GPS und
  Aufnahmezeit werden in Spalten übernommen; ausgelieferte Varianten enthalten keine EXIF-Daten.
- Antivirus-Prüfung **[Offen]** optional über einen ClamAV-Container (nicht im Standard-Compose).

## 8. Docker-Betrieb

| Container | Image | Volumes | Netz |
|---|---|---|---|
| `caddy` | caddy:2 | `caddy_data` | Ports 80/443 nach außen |
| `api` | eigenes Image (Distroless/Chiseled .NET) | `/data/files` → Host-Pfad | intern |
| `postgres` | postgres:17-alpine | `/var/lib/postgresql/data` → Host-Pfad | nur intern |
| `backup` | eigenes Image (restic, pg_dump, Skripte) | liest Dateien und Datenbank, schreibt aufs Backup-Ziel | intern |
| `migrate` | wie `api`, Befehl `migrate` | – | einmalig vor Start |

- **TLS [Vorschlag]:** Caddy holt öffentliche Zertifikate automatisch (Let's Encrypt). Für reine LAN-Server
  ohne öffentliche Domain gibt es den Modus „interne CA“. Dann muss das CA-Zertifikat auf den Geräten
  installiert werden; das ist dokumentiert, aber unbequem. Empfohlen wird eine öffentliche Domain mit
  DNS-Challenge, auch wenn der Server nur intern erreichbar ist.
- **Daten liegen auf Host-Pfaden** (`/srv/estrichone/…`) und nicht in anonymen Volumes. So ist für den Betreiber
  sichtbar, was gesichert werden muss.
- Container laufen ohne Root, mit `read_only`, `cap_drop: ALL` und `no-new-privileges`. Dieses Muster ist in
  SATZIO bereits erprobt.
- **Mindestgröße:** 2 vCPU, 4 GB RAM, SSD. Speicherbedarf ist im Wesentlichen Fotos × Aufbewahrung.

## 9. Mobile Clients

```
UI (SwiftUI / Compose)
 └─ Feature-ViewModels
     └─ Repository (liest IMMER aus der lokalen DB; schreibt lokal + Outbox in EINER Transaktion)
         ├─ lokale DB je Benutzer (GRDB / Room, verschlüsselt)
         ├─ Outbox (Operationen) + Upload-Warteschlange (Dateien)
         └─ SyncEngine (push → pull, Backoff, Hintergrundausführung)
              └─ generierter API-Client
```

Die Oberfläche wartet nie auf das Netz. „Gespeichert“ bedeutet lokal und dauerhaft gespeichert. Der
Synchronisationsstatus wird zusätzlich je Datensatz angezeigt (15).

## 10. Offene Architekturentscheidungen

| ID | Frage | Vorschlag |
|---|---|---|
| E-01 | Backend-Technologie | ✅ ASP.NET Core (.NET 10 LTS) – entschieden |
| E-02 | Web-Framework | Angular, Neuaufbau |
| E-03 | Datenbank | PostgreSQL 17 (verbindlich bevorzugt) |
| E-04 | Mandantenfähigkeit | entfällt, eine Installation = ein Betrieb |
| E-05 | Bauakte/Baustelle | ✅ zusammenführen, Bereiche für Bauabschnitte – entschieden |
| E-06 | Reverse Proxy | Caddy |
| E-07 | Push-Benachrichtigungen | optional (APNs/FCM benötigen Konten, siehe 20, Abschnitt 6); Standard: In-App + E-Mail |
| E-08 | Original-Fotos behalten | ja, standardmäßig |
| E-09 | PDF-Bibliothek | MIT-lizenziert (PDFsharp/MigraDoc) |
