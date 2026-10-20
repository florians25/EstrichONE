# EstrichONE Hosting-Konsole

Verwaltungskonsole für einen Hosting-Server, der viele EstrichONE-Instanzen (Mandanten) betreibt. Jede Instanz ist ein
eigenes Docker-Compose-Projekt `estrichone-<kürzel>` mit eigenem Verzeichnis, eigener `.env`, zufälligen Passwörtern und
eigenen Netzen. Ein gemeinsamer Reverse Proxy (mitgelieferter Caddy) verteilt die Anfragen per Domain; die Instanzen
veröffentlichen keine Ports.

```
Internet ──80/443──▶ Caddy (estrichone-hosting-proxy) ──┬─ konsole.<basis>     ▶ Konsole (estrichone-hosting-console)
                                                        ├─ firma-a.<basis>     ▶ estrichone-firma-a-caddy-1:8181 (Netz estrichone-firma-a-edge)
                                                        └─ firma-b.example.de  ▶ estrichone-firma-b-caddy-1:8181 (Netz estrichone-firma-b-edge)
```

## Funktionen

- **Mandanten**: anlegen (Firma, Kunden-Nr., Ansprechpartner, E-Mail, Telefon, Kürzel, Subdomain oder eigene Domain,
  Tarif, Version), bearbeiten (Domain-/Tarifwechsel wird sofort angewendet), Notizen.
- **Lebenszyklus**: bereitstellen, starten, stoppen, neu starten, auf Version X aktualisieren (einzeln oder alle, mit
  Vorab-Sicherung), sperren (Wartungsseite, z. B. bei Zahlungsverzug; optional zusätzlich anhalten), entsperren,
  jetzt sichern, löschen (doppelte Bestätigung, optional Endsicherung mit Schlüssel ins Archiv). Jeder Vorgang läuft
  im Hintergrund mit Live-Protokoll.
- **Betrieb**: Container-Zustand und Health je Dienst, `/health/ready`, laufende Version, Ersteinrichtung ausstehend
  mit **Einrichtungscode** zum Weitergeben, CPU/RAM (docker stats), Speicherplatz, aktive Benutzer gegen Tariflimit,
  Sicherungsstatus (restic: letzte Sicherung, Prüfung, Wiederherstellungstest), Protokolle (Dienst, Zeilen, Text- und
  Stufenfilter, Live, Download), Sicherungsschlüssel anzeigen (mit Passwortabfrage, protokolliert).
- **Hinweise und Wartung**: Ankündigungen (Titel, Text, Schweregrad, Zeitraum) und Wartungsfenster (Start/Ende,
  Administratoren dürfen weiterarbeiten) an einzelne, ausgewählte oder alle Instanzen. Die Suite zeigt sie als Banner
  bzw. Wartungsseite (Suite-Modul „Systemhinweise“, `GET /api/v1/system/notices`). Die Konsole gleicht jede Minute ab.
- **Übersicht**: Kennzahlen (Mandanten aktiv/gesperrt/Fehler, veraltete Versionen, Speicher, Benutzer, veraltete
  Sicherungen), Handlungsbedarf, Serverzustand, Versionen im Einsatz, letzte Ereignisse; Liste mit Suche und Filtern.
- **Tarife**: Benutzer- und Speicherlimit (überwacht), RAM- und CPU-Grenze für die API (durchgesetzt), Preis.
- **Ereignisprotokoll** aller Konsolenaktionen mit Suche, Zeitraum, Ergebnisfilter und CSV-Export.
- **Sicherheit**: eigene Administratoren (Passwort-Hash PBKDF2, Sitzungscookie `__Host-` Secure/HttpOnly/SameSite=Strict,
  CSRF-Token, Origin-Prüfung, Login-Rate-Limit je Adresse, Kontosperre nach 5 Fehlversuchen, TOTP-2FA mit QR-Code),
  Ersteinrichtung per Einmal-Code im Protokoll, strikte CSP (nur eigene Skripte), Docker-Socket nur im Konsolen-Container.
- **Oberfläche**: OpenUI5 (TypeScript, self-hosted), alle Themes der Version (Horizon, Horizon Dark, Fiori 3 + Dark,
  Belize + Deep, alle High-Contrast-Varianten) mit „Systemeinstellung folgen“, Live-Umschaltung und Speicherung je
  Benutzer; ausschließlich das EstrichONE-Logo (je Theme die passende Variante aus `branding/`).
- **Selbst-Update** der Konsole über Einstellungen → System.

## Voraussetzungen

- Linux-Server mit Docker Engine ≥ 24 und Compose-Plugin; je Instanz grob 1 GB RAM und 0,5 vCPU zusätzlich.
- Ports **80 und 443** frei (mitgelieferter Caddy) – oder ein vorhandener Reverse Proxy (siehe unten).
- **DNS**: A/AAAA-Eintrag für die Konsolen-Domain und ein **Wildcard-Eintrag** `*.<basis-domain>` auf den Server.
  Eigene Kunden-Domains per CNAME auf `<kürzel>.<basis-domain>`. Zertifikate holt Caddy automatisch (Let's Encrypt).
- Viele Instanzen: jede braucht drei Docker-Netze. Für mehr als ~80 Instanzen in `/etc/docker/daemon.json` z. B.
  `"default-address-pools":[{"base":"10.200.0.0/14","size":24}]` setzen und Docker neu starten.

## Installation (ein Befehl)

```bash
sudo docker run --rm -it \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v /srv/estrichone-hosting:/srv/estrichone-hosting \
  ghcr.io/florians25/estrichone-hosting:latest install
```

Der Installer fragt Basis-Domain, Konsolen-Domain, Proxy-Art, Zertifikate und E-Mail für Let's Encrypt ab, legt
`/srv/estrichone-hosting/.env` (nur root lesbar) und `compose.yml` an, startet Konsole und Proxy und zeigt den
**Einrichtungscode** für den ersten Administrator. Ohne Rückfragen:
`-e HOSTING_BASE_DOMAIN=estrichone-hosting.de -e HOSTING_ACME_EMAIL=admin@firma.de` (weitere Variablen siehe
`deploy/env.example`). Den Code zeigt jederzeit `docker logs estrichone-hosting-console` (nur solange kein Administrator existiert).

**Konsole aktualisieren:** in der Konsole unter Einstellungen → System → „Konsole aktualisieren“ oder denselben
Befehl mit neuem Image-Tag erneut ausführen. `.env`, Datenbank und alle Mandanten bleiben erhalten.

| Pfad | Inhalt |
|---|---|
| `/srv/estrichone-hosting/instances/<kürzel>/` | Compose-Dateien, `.env` (Passwörter, Sicherungsschlüssel), `data/`, `backup-repository/` |
| `/srv/estrichone-hosting/proxy/` | von der Konsole erzeugtes Caddyfile und Wartungsseite |
| `/srv/estrichone-hosting/console/` | Datenbank der Konsole (SQLite), Sitzungsschlüssel – nur root |
| `/srv/estrichone-hosting/archive/` | Endsicherungen gelöschter Mandanten (restic-Repository + `instanz.env`) |

| Port | Zweck |
|---|---|
| 80 / 443 (TCP, 443 auch UDP) | gemeinsamer Caddy: Konsole und alle Instanzen, HTTPS |
| 127.0.0.1:8099 | Notzugang zur Konsole direkt auf dem Server (SSH-Tunnel) |

## Ablauf: Mandant anlegen

1. Konsole öffnen → **Mandanten** → **Mandant anlegen**.
2. Firmenname eingeben – Kürzel und Subdomain werden vorgeschlagen (`estrichbau-muster` → `estrichbau-muster.<basis>`);
   bei eigener Domain diese eintragen und beim Kunden einen CNAME setzen lassen.
3. Tarif und Version wählen, **Anlegen**. Die Konsole legt Verzeichnis, `.env` mit Zufallspasswörtern, Netze und
   Proxy-Eintrag an, lädt die Images und startet die Instanz (Live-Protokoll unter „Laufender Vorgang“).
4. Sobald „Aktiv“: Im Abschnitt **Ersteinrichtung** steht der Einrichtungscode. Dem Kunden Adresse + Code geben;
   er legt in EstrichONE über „Jetzt einrichten“ Firma und ersten Administrator an.
5. Betrieb: Übersicht und Liste zeigen Zustand, Sicherungen und Limits; Aktionen auf der Detailseite.

## Anbindung an einen vorhandenen Proxy (Nginx Proxy Manager)

Installer mit `HOSTING_PROXY=extern` und `HOSTING_PROXY_CONTAINER=<name des NPM-Containers>` ausführen. Der Caddy der
Konsole wird dann nicht gestartet; die Konsole verbindet den NPM-Container mit jedem Instanznetz. Je Mandant in NPM
einen Proxy Host anlegen: Domain → `http://estrichone-<kürzel>-caddy-1:8181` (Detailseite → Technik → Proxy-Ziel),
SSL mit Let's Encrypt und **Force SSL**, unter Advanced `client_max_body_size 600m;`. Für die Konsole:
`docker network connect estrichone-hosting <npm-container>` und Proxy Host → `http://estrichone-hosting-console:8080`.
Sperren hält in diesem Modus die Weboberfläche der Instanz an (keine eigene Wartungsseite).

## Sicherheitshinweise

- Der Docker-Socket ist nur im Konsolen-Container eingebunden; wer die Konsole bedient, kann den Host steuern.
  Konsole nur Administratoren zugänglich machen, 2FA einschalten.
- Docker wird ohne Shell mit Argumentlisten aufgerufen; Kürzel, Domains und Versionen werden streng geprüft.
- Der Konsolen-Container läuft schreibgeschützt mit minimalen Capabilities (CHOWN, DAC_OVERRIDE, DAC_READ_SEARCH,
  FOWNER), um Datenverzeichnisse der Instanzen (UID 1654) anzulegen, zu messen und zu löschen.
- Die `.env` jeder Instanz enthält Datenbank-, Sicherungs- und Hinweis-Schlüssel – in Server-Backups verschlüsselt sichern.

## Entwicklung

```bash
cd hosting
dotnet test --solution EstrichOne.Hosting.slnx          # Unit- und Integrationstests (simulierter Docker)
cd ui && npm ci && npm run build                          # Oberfläche → ui/dist (in wwwroot kopieren)
Hosting__Docker=fake Hosting__Root=/tmp/hosting dotnet run --project ../src/EstrichOne.Hosting   # Simulation
npx playwright test                                       # E2E gegen http://localhost:5090
IMAGE=estrichone/hosting:ci hosting/scripts/integration.sh  # echter Test mit Docker und Suite-Images
```
