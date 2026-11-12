# EstrichONE – Installation und Betrieb

## Voraussetzungen

- Linux-Server (z. B. Ubuntu 24.04 oder Debian 12), mind. 2 vCPU, 4 GB RAM, SSD
- Docker Engine ≥ 24 mit Compose-Plugin
- Für den Zugriff aus dem Internet bzw. von Smartphones: eine Domain, die auf den Server zeigt,
  und offene Ports 80 und 443

## Installation mit einem Befehl (empfohlen)

Auf einem frischen Linux-Server (Ubuntu 24.04 LTS oder Debian 12) genügt:

```bash
curl -fsSL https://raw.githubusercontent.com/florians25/EstrichONE/main/install.sh | sudo bash
```

Das Skript

1. **prüft den Server** – Betriebssystem, Prozessor, Arbeitsspeicher (mind. 2 GB, empfohlen 4 GB), freien Speicher
   (mind. 8 GB, empfohlen 30 GB), Docker mit Compose-Plugin, belegte Ports, DNS-Eintrag der Domain und die Firewall
   (ufw). Jede Meldung nennt die Abhilfe. Zeigt die Domain nicht auf eine Adresse des Servers, fragt das Skript kurz
   (je höchstens 5 Sekunden) die **öffentliche Adresse** ab (ipify, ifconfig.me, icanhazip; IPv4 und IPv6): Hinter einem
   Router bzw. Cloud-NAT ist der Eintrag dann richtig und es gibt keine Warnung, nur den Hinweis auf die
   Portweiterleitung. Ohne Internet bleibt es bei einem Hinweis, nie ein Abbruch. `ESTRICHONE_PUBLIC_IP=<adresse>` gibt
   die Adresse fest vor, `ESTRICHONE_PUBLIC_IP=aus` schaltet die Abfrage ab;
2. **installiert Docker**, falls es fehlt (Debian/Ubuntu, nach Rückfrage bzw. mit `--docker-installieren`);
3. fragt nur **Adresse** und **Firmenname** ab (Enter übernimmt den Vorschlag) sowie, ob Sicherheitsupdates nachts
   automatisch eingespielt werden sollen (Vorschlag: ja);
4. wählt die **Betriebsart** selbst: Sind 80 und 443 frei, holt EstrichONE das Zertifikat selbst bei Let's Encrypt
   (`direkt` auf 80/443). Sind sie belegt, läuft EstrichONE hinter dem vorhandenen Proxy (`proxy`, HTTP auf 8181). Ist
   der vorhandene Proxy ein **Caddy auf dem Server** (Paket, systemd), trägt das Skript EstrichONE auf Wunsch dort als
   Site ein – das Zertifikat holt dann dieser Caddy. Beim Nginx Proxy Manager zeigt es die nötigen Einstellungen;
5. startet den Installer der neuesten Version, legt `/opt/estrichone/update.sh` an und zeigt am Ende **Adresse,
   Backup-Passwort und Einrichtungscode**.

**Erneut ausführen = Reparatur:** Bei einer vorhandenen Installation fragt das Skript nichts, bleibt bei der installierten
Version und stellt Compose-Dateien, Rechte und Dienste wieder her. Daten, `.env` und Passwörter bleiben unverändert.

| Befehl | Wirkung |
|---|---|
| `curl -fsSL …/install.sh \| sudo bash` | installieren bzw. reparieren |
| `curl -fsSL …/install.sh \| sudo bash -s -- pruefen` | nur prüfen, nichts verändern |
| `curl -fsSL …/install.sh \| sudo bash -s -- aktualisieren [VERSION]` | aktualisieren mit Sicherung (= `update.sh`) |
| `curl -fsSL …/install.sh \| sudo bash -s -- wiederherstellen` | Neuinstallation aus einer Sicherung (Notfallblatt) |
| `curl -fsSL …/install.sh \| sudo bash -s -- archiv` (bzw. `backup-ziel`, `kombi`) | weiteren Rechner einrichten |
| `curl -fsSL …/install.sh \| sudo bash -s -- konsole` | Hosting-Konsole für viele Instanzen (siehe `hosting/README.md`) |

**Ohne Rückfragen** (Automatisierung): `--ja` und die Angaben als Optionen oder Umgebungsvariablen:

```bash
curl -fsSL https://raw.githubusercontent.com/florians25/EstrichONE/main/install.sh | sudo bash -s -- \
  --ja --docker-installieren --domain estrichone.firma.de --name "Estrichbau Muster GmbH"
```

Weitere Optionen: `--modus direkt|proxy`, `--version 1.0.13`, `--auto-updates sicherheit|patch|aus`,
`--ohne-pruefung` (Mindestanforderungen nur melden). Alle `ESTRICHONE_*`-Variablen des Installers (z. B.
`ESTRICHONE_HTTP_PORT`, `ESTRICHONE_BIND_ADDRESS`, `ESTRICHONE_TRUSTED_PROXIES`, `ESTRICHONE_ARCHIVE=lokal`) reicht das
Skript weiter.

Typische Meldungen und Abhilfe:

| Meldung | Abhilfe |
|---|---|
| `Port 80 ist bereits belegt (nginx)` | Dienst beenden oder `--modus proxy` (EstrichONE dann hinter diesem Proxy) |
| `DNS: … ist (noch) nicht auflösbar` | A-/AAAA-Eintrag beim DNS-Anbieter anlegen; das Zertifikat folgt automatisch |
| `DNS: … (öffentliche Adresse dieses Servers, Netz mit NAT bzw. Router)` | kein Fehler: Server hinter Router/Cloud-NAT; dort 80 und 443 auf den Server weiterleiten bzw. freigeben |
| `DNS: … – dieser Server ist aber unter … erreichbar` | DNS-Eintrag auf die genannte öffentliche Adresse ändern (oder bewusst anderer Proxy/Load-Balancer davor) |
| `… die öffentliche Adresse ist nicht ermittelbar (offline?)` | nur ein Hinweis; bei Servern ohne Internet `ESTRICHONE_PUBLIC_IP=<adresse>` setzen |
| `Firewall (ufw) … lässt Port(s) 80 443 nicht zu` | `sudo ufw allow 80/tcp` und `sudo ufw allow 443/tcp` |
| `Installer-Image … nicht ladbar` | Internetverbindung, Version bzw. Docker-Proxy im Firmennetz prüfen |

## Installation mit einem Docker-Befehl

Gleichwertig, wenn Docker bereits installiert ist (das Skript oben ruft genau diesen Installer auf):

```bash
sudo docker run --rm -it \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v /opt/estrichone:/opt/estrichone \
  ghcr.io/florians25/estrichone-installer:latest
```

Der Installer fragt **Betriebsart**, Domain und Firmennamen ab, legt `/opt/estrichone` mit `.env` (zufällige
Passwörter) an, lädt die fertigen Images und startet die Suite. Er zeigt am Ende das **Backup-Passwort** und den
**Einrichtungscode** für den ersten Administrator.

### Betriebsart `proxy` (Standard) – hinter Nginx Proxy Manager o. Ä.

EstrichONE stellt nur **HTTP auf Port 8181** bereit – ohne Weiterleitung und ohne eigenes Zertifikat. HTTPS, Domain
und Zertifikat übernimmt der vorhandene Reverse Proxy. Im Nginx Proxy Manager:

| Einstellung | Wert |
|---|---|
| Domain Names | die beim Installer angegebene Domain, z. B. `estrichone.firma.de` |
| Scheme / Forward Hostname / Port | `http` / IP des EstrichONE-Servers / `8181` |
| SSL | Let's-Encrypt-Zertifikat, **Force SSL** an (die Anmeldung im Web setzt HTTPS voraus) |
| Advanced (für große Videos) | `client_max_body_size 600m;` |

`X-Forwarded-For`/`-Proto` werden nur von privaten Adressen (LAN, Docker-Netze) übernommen; die Protokolle zeigen
dadurch die echte Client-Adresse.

Läuft der Proxy auf einem anderen Rechner mit **öffentlicher** Adresse, diese in der `.env` eintragen –
`ESTRICHONE_TRUSTED_PROXIES="private_ranges 203.0.113.5"` (mehrere Adressen/Netze mit Leerzeichen) bzw. beim Installer
`-e ESTRICHONE_TRUSTED_PROXIES=203.0.113.5` (ergänzt `private_ranges` selbst). Die Liste gilt für Caddy **und** die API
(`EstrichOne__TrustedProxies`): Ohne Eintrag sähen Anmeldesperre und Rate-Limits nur die Adresse des Proxys, mit einer
zu großzügigen Liste (`0.0.0.0/0`) könnten Angreifer fremde Adressen vortäuschen. Danach `docker compose up -d`.

### Betriebsart `direkt` – ohne vorgeschalteten Proxy

| Port | Zweck |
|---|---|
| **8143** | HTTPS – Weboberfläche und API (`https://<adresse>:8143`) |
| **8181** | HTTP – leitet auf 8143 weiter |

Bei IP-Adresse oder `localhost` stellt Caddy ein eigenes Zertifikat aus (der Browser warnt einmal; die Android-App
akzeptiert es nicht). Für ein gültiges Let's-Encrypt-Zertifikat eine Domain verwenden und am Router
**80 → 8181** und **443 → 8143** weiterleiten.

### Optionen

- Ohne Rückfragen: `-e ESTRICHONE_MODE=proxy -e ESTRICHONE_DOMAIN=estrichone.firma.de -e ESTRICHONE_NAME="Estrichbau Muster GmbH"`
- Andere Ports: `-e ESTRICHONE_HTTP_PORT=8181 -e ESTRICHONE_HTTPS_PORT=8143`
- **Aktualisieren:** `sudo /opt/estrichone/update.sh` (siehe „Aktualisieren“ unten) – oder denselben Befehl mit
  neuer Version erneut ausführen (Daten und Konfiguration bleiben). Betriebsart wechseln: zusätzlich
  `-e ESTRICHONE_MODE=proxy` bzw. `direkt`.
- Archivserver im selben Stack (nur ohne zweiten Rechner): `-e ESTRICHONE_ARCHIVE=lokal`
- Verwalten: `cd /opt/estrichone && sudo docker compose -p estrichone ps|logs|down`

## Installation aus dem Quellcode

```bash
git clone https://github.com/florians25/EstrichONE.git estrichone
cd estrichone/deploy
sudo ./setup.sh                      # fragt Adresse und Firmennamen ab, erzeugt .env mit sicherem Passwort
sudo docker compose up -d --build    # baut und startet Datenbank, Migration, API, Weboberfläche und Reverse Proxy
curl https://<ihre-adresse>/api/v1/server-info
```

Hinter einem eigenen Reverse Proxy: in der `.env` `COMPOSE_FILE=compose.yml:compose.proxy.yml`,
`ESTRICHONE_HTTP_PORT=8181` und `ESTRICHONE_PUBLIC_URL=https://<domain>` setzen (siehe `compose.proxy.yml`).

## Ersteinrichtung (erster Administrator)

Solange es keinen Benutzer gibt, schreibt der Server beim Start einen **Einrichtungscode** ins Protokoll
(24 Stunden gültig, einmalig; jeder Neustart erzeugt einen neuen Code):

```bash
sudo docker compose logs api | grep Einrichtungscode
```

Im Browser `https://<ihre-adresse>/` öffnen, „Einrichtung starten“ wählen und mit diesem Code Firmenname und
ersten Administrator anlegen. Alternativ per API:

```bash
curl -X POST https://<ihre-adresse>/api/v1/setup -H 'Content-Type: application/json' -d '{
  "setupCode": "ABCD-EFGH-JKLM",
  "companyName": "Estrichbau Muster GmbH",
  "administrator": { "displayName": "Anna Admin", "userName": "admin", "email": null,
                     "password": "eine lange Passphrase" }
}'
```

Danach ist die Einrichtung dauerhaft gesperrt. Es gibt keine Standardpasswörter.

`setup.sh` überschreibt eine vorhandene `.env` nie. **Bewahren Sie die `.env` zusätzlich sicher auf**
(Passwortmanager). Sie enthält das Datenbankpasswort und ist bewusst nicht Teil des Backups.

## Was läuft

| Dienst | Aufgabe | Von außen erreichbar |
|---|---|---|
| `caddy` | Weboberfläche (Image `estrichone/web`), HTTPS, Zertifikate, Weiterleitung an die API | ja (80, 443) |
| `api` | EstrichONE-Server; aktualisiert beim Start das Datenbankschema | nein |
| `postgres` | Datenbank | nein (internes Netz ohne Internetzugang) |
| `backup` | tägliche verschlüsselte Sicherung (restic), Prüfung, Wiederherstellungstest | nein |
| `restore` | nur bei Bedarf: Wiederherstellung (Profil `restore`) | nein |
| `archive` | nur mit Profil `archive`: Archivserver im selben Stack (besser: eigener Rechner) | nein |

Alle Daten liegen unter `ESTRICHONE_DATA_DIR` (Standard `deploy/data`):

```
data/postgres   Datenbank
data/files      Fotos, Dokumente, Videos
data/caddy      Zertifikate
```

## Zertifikate

- **Öffentliche Domain:** Caddy holt das Zertifikat automatisch von Let's Encrypt. Ports 80 und 443 müssen aus
  dem Internet erreichbar sein.
- **Nur im Firmennetz:** Entweder eine öffentliche Domain mit DNS-Challenge verwenden (empfohlen, siehe
  Caddy-Dokumentation zu DNS-Modulen) oder `localhost`/interne Namen mit Caddys interner Zertifizierungsstelle.
  Im zweiten Fall muss das Wurzelzertifikat (`data/caddy/data/caddy/pki/authorities/local/root.crt`) auf allen
  Geräten installiert werden. Die Apps akzeptieren keine ungültigen Zertifikate.

## Zustand prüfen

```bash
docker compose ps                     # alle Dienste „healthy“
docker compose logs -f api            # Protokoll (JSON, ohne personenbezogene Inhalte)
curl https://<adresse>/health/ready   # 200 = API und Datenbank bereit
```

## Sicherung (Backup)

Ausführlich – Topologien (RZ ↔ zuhause), Backup Center, Wiederherstellen, Worst Case, Notfallblatt: **[docs/backup.md](../docs/backup.md)**.

Der Dienst `backup` sichert **täglich um 22:30** (`BACKUP_TIME`, im Backup Center änderbar) voll: Datenbank, Dateien,
Schlüsselbund und Konfiguration, verschlüsselt mit restic; optional zusätzlich inkrementell.
Sonntags kommen dazu: Aufbewahrungsregeln anwenden, Integrität prüfen und **eine echte Wiederherstellung in eine
Testdatenbank**. Den Zustand zeigt `GET /api/v1/backup/status` (später das Backup Center). `/health/ready` meldet
„Degraded“, wenn die letzte erfolgreiche Sicherung älter als 36 Stunden ist.

| Einstellung (.env) | Bedeutung |
|---|---|
| `RESTIC_PASSWORD` | Schlüssel der Sicherungen – **von setup.sh angezeigt, außerhalb des Servers aufbewahren** |
| `ESTRICHONE_BACKUP_DIR` | Hauptziel auf dem Host, idealerweise zweite Festplatte oder eingebundenes NAS |
| `BACKUP_REPOSITORY_2` | **dringend empfohlen:** zweites Ziel an anderem Ort, z. B. `sftp:backup@nas2.firma.de:/estrichone` (Schlüssel in `ESTRICHONE_SSH_DIR`) |

Eine Kopie der `.env` (`data/config/estrichone.env`, von setup.sh bzw. dem Installer angelegt) ist Teil jeder Sicherung –
damit kann der Installer-Modus `restore` einen neuen Server vollständig einrichten. Das Backup-Passwort selbst gehört
zusätzlich auf das Notfallblatt (Backup Center → Notfall) bzw. in einen Passwortmanager.

```bash
docker compose exec backup estrichone-backup run           # jetzt sichern
docker compose exec backup estrichone-backup snapshots     # Sicherungen anzeigen
docker compose exec backup estrichone-backup check 100%    # alles vollständig prüfen
```

## Wiederherstellen (auch auf einem neuen Server)

Am einfachsten: im Backup Center (*Sicherungen* → *Wiederherstellen*) bzw. auf einem neuen Server mit dem Installer-Modus
`restore` ([docs/backup.md](../docs/backup.md#worst-case-server-komplett-ausgefallen)). Von Hand mit diesem Repository:

Voraussetzungen: Docker, dieses Repository, die gesicherte `.env` (bzw. `RESTIC_PASSWORD`) und Zugriff auf das
Backup-Ziel.

```bash
sudo ./setup.sh                                   # .env zurückkopieren statt neu erzeugen, falls vorhanden
docker compose stop api backup                    # bzw. auf neuem Server: docker compose up -d --wait postgres
docker compose --profile restore run --rm restore latest   # oder eine Snapshot-ID aus „snapshots“
docker compose up -d
```

Die Wiederherstellung prüft zuerst das Repository, ersetzt dann Datenbank und Dateien und vergibt eine neue
Instanzkennung. Die Apps erkennen das und gleichen vollständig neu ab. Ungesendete Änderungen auf den Geräten
bleiben erhalten. Der komplette Ablauf wird in der CI bei jeder Änderung und nächtlich geprüft
(`deploy/backup/test-backup-restore.sh`).

## Aktualisieren

Der Installer legt `/opt/estrichone/update.sh` an. Auf dem Server genügt:

```bash
sudo /opt/estrichone/update.sh          # neueste veröffentlichte Version
sudo /opt/estrichone/update.sh 0.1.0    # bestimmte Version (auch zurück auf eine ältere)
```

Das Skript zeigt „Installiert → Neu“, lädt die Images vorab, kündigt das Update an (siehe „Update-Ablauf“), **sichert
vorher** (Backup-Dienst; bricht bei Fehler ab, ohne Sicherung: `--ohne-sicherung`), führt den Installer der Zielversion aus (Konfiguration und Daten bleiben), wartet, bis alle
Dienste gesund sind, prüft die laufende Version und meldet klar Erfolg oder Fehler. Installationen vor 0.1.0 haben
das Skript noch nicht: einmal den Installer-Befehl von oben mit der neuen Version ausführen, danach ist es da.

### Update-Ablauf (mindestens 3 Minuten)

Jedes Update läuft in drei Phasen – mit `update.sh`, `install.sh aktualisieren`, dem Installer auf einer laufenden
Installation (andere Version) und der Hosting-Konsole (Einzel- und Massen-Update) gleich:

| Phase | Dauer | Was passiert |
|---|---|---|
| 1. Ankündigung | 60 s | Alle angemeldeten Benutzer sehen oben „Update auf Version X beginnt in 0:59 – bitte Arbeit speichern“ (Web und Apps). Die Apps übertragen offene Änderungen sofort. Neue Anmeldungen sind gesperrt. |
| 2. Update | mindestens 60 s | Die API beendet alle Sitzungen (auch Administratoren) und sperrt alles außer Hinweisen und Serverinfo. Web und Apps zeigen den Wartungsbildschirm mit Phase, Zielversion und Fortschritt. Sicherung, Austausch der Container, Warten auf `/health/ready`. |
| 3. Abschluss | 60 s | Die neue Version läuft; der Wartungsbildschirm zeigt „Freigabe in 0:59“. Danach Freigabe: das Web lädt neu und zeigt die Anmeldung mit „EstrichONE wurde auf Version X aktualisiert“, die Apps führen zur Anmeldung (Daten bleiben auf dem Gerät). |

Der Stand liegt in `data/backup-status/update-maintenance.json`. Die API liest ihn bei jeder Anfrage (Kopfzeile
`X-EstrichONE-Update` auf jeder Antwort, Feld `update` in `/api/v1/system/notices`); Caddy liefert ihn zusätzlich als
`/update-status.json` aus dem nur lesend eingebundenen Verzeichnis – so bleibt der Wartungsbildschirm im Web auch dann
stehen, wenn die API gerade getauscht wird (statt „nicht erreichbar“). Die Störungsseite (`/stoerung.html`, OpenUI5 wie die App) zeigt
Phase und Countdown ebenso.

- **Dauer anpassen** (Sekunden, Umgebung oder `.env`): `ESTRICHONE_UPDATE_ANNOUNCE_SECONDS`, `ESTRICHONE_UPDATE_MIN_SECONDS`,
  `ESTRICHONE_UPDATE_FINISH_SECONDS` – Standard je 60. Kürzer nur für Tests (CI: `deploy/test-update-ablauf.sh`).
- **Fehlschlag:** Läuft EstrichONE weiter (z. B. Sicherung fehlgeschlagen), sehen die Benutzer „Update fehlgeschlagen –
  Version X läuft weiter“ und nach der Abschlussdauer die Freigabe. Startet nichts mehr, bleibt der Wartungsbildschirm
  mit Meldung stehen. Nach der Reparatur freigeben: `sudo /opt/estrichone/update.sh --wartung-beenden`. Ein verwaister
  Stand (abgebrochenes Skript) gilt nach 6 Stunden automatisch nicht mehr.
- **Erstes Update nach Einführung:** Der Ablauf gilt ab der Version, die ihn mitbringt – das `update.sh` einer älteren
  Installation spielt das erste Update noch ohne Phasen ein.
- Läuft EstrichONE beim Update nicht (angehalten), entfällt die Ankündigung. Eine Reparatur mit derselben Version
  (Installer erneut) tauscht keine Container und kündigt nichts an.

**Sicherheitsupdates automatisch** (empfohlen, docs/sicherheit.md → „Sicherheitsupdates“):

```bash
sudo /opt/estrichone/update.sh --automatik sicherheit            # nur Sicherheitsupdates, nachts 02:00-05:00
sudo /opt/estrichone/update.sh --automatik patch 01:00-04:00     # alle Fehlerbehebungen derselben Versionslinie
sudo /opt/estrichone/update.sh --pruefen                         # nur nachsehen
```

Stündliche Prüfung per systemd-Timer (bzw. `/etc/cron.d`), Einspielen nur im Zeitfenster, Sicherung vorher. Neue
Versionslinien (z. B. 0.1 → 0.2) werden nie automatisch eingespielt. Administratoren sehen im Web den Hinweis
„Sicherheitsupdate verfügbar“. Für den Betrieb im Internet: Checkliste „Öffentlich betreiben“ in docs/sicherheit.md
(Firewall, nur 443, Nginx-Proxy-Manager-Einstellungen, `ESTRICHONE_BIND`).

**Apps:** Das Image der API enthält die signierte Android-App derselben Version (mit Prüfsumme) und die
Versionshinweise. Nach dem Update zeigen die Apps „Update verfügbar“; Android lädt das Update direkt vom Server,
iPhone/iPad über App Store, TestFlight oder MDM. Regeln (Hinweis, erzwungene Mindestversion, iOS-Link) und die
Geräteübersicht stehen im Web unter Benutzer & Geräte → App-Updates. Für die Apps muss der Server unter seiner
öffentlichen Adresse `/api/v1/app-updates/…` ohne Anmeldung ausliefern dürfen (Standard im Caddy-Stack; bei Nginx Proxy
Manager keine zusätzliche Zugriffsbeschränkung auf diesen Pfad). Einzelheiten: docs/app-updates.md.

Aus dem Quellcode:

```bash
docker compose exec backup estrichone-backup run   # vorher sichern
git pull
docker compose up -d --build                        # die API aktualisiert das Schema beim Start
```

Archivserver und Backup-Ziele auf anderen Rechnern: dort den jeweiligen Installationsbefehl erneut ausführen.

## Archivserver (10 Jahre, revisionssicher)

Unveränderbare Ablage mit Prüfsummen, Hash-Kette, Löschsperre bis Fristende, Prüfprotokoll und Export für den
Betriebsprüfer – auf einem zweiten Rechner mit **einem** Befehl:

```bash
sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest archiv
```

Danach in EstrichONE: **System → Archiv → „Archivserver koppeln“**, Adresse und Kopplungscode eingeben, fertig.
Regeln (welche Dokumentart, wie lange, wann) stehen auf derselben Seite. Ausführlich: [archive/README.md](archive/README.md).

## Backup-Ziel auf einem zweiten Rechner

Zusätzliche Sicherung auf NAS/PC/Raspberry Pi, **nur anhängen** (Löschen unmöglich, Schutz vor Trojanern):

```bash
sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest backup-ziel
```

Dann **System → Backup-Ziele → „Backup-Ziel koppeln“** und den **Notfallzugang** herunterladen und mit dem
Backup-Passwort aufbewahren. Ausführlich: [backup-target/README.md](backup-target/README.md).

## Kombiserver: Backup-Ziel und Archiv auf einem Rechner

```bash
sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone-kombi:/opt/estrichone-kombi \
  ghcr.io/florians25/estrichone-installer:latest backup-archiv
```

Ein Lauf, getrennte Container und Daten, eine `.env` (600) unter `/opt/estrichone-kombi`, **ein** Kopplungscode für beide:
**System → Backup-Ziele → „Kombiserver koppeln“**. Kein zweiter Standort für das Archiv – räumlich getrennt aufstellen.
Ausführlich: [../docs/backup.md](../docs/backup.md) („Kombiserver“).

## Überwachung

**System → Überwachung** zeigt Produktivsystem, Archivserver und alle Backup-Ziele: Plattenbelegung, freier Platz und
Trend (Zuwachs pro Tag, „voll in ca. … Tagen“), Dienste (Datenbank, Sicherung, Archiv-Warteschlange, Erreichbarkeit),
letzte Sicherung, letzte Archivierung und letzte Archivprüfung. Warnschwellen: Platte ab 85 % (kritisch ab 95 % oder
voll in weniger als 14 Tagen), Sicherung älter als 36 Stunden, Archivprüfung mit Abweichungen. Bei Warnungen erscheint
ein Hinweis auf der Startseite (für Benutzer mit dem Recht „Backups verwalten“).

## E-Mail-Konto

**System → E-Mail:** SMTP-Server (z. B. `smtp.ionos.de`, `smtp.office365.com`, `smtp.strato.de`), Port und
Verschlüsselung (587 STARTTLS oder 465 SSL), Benutzer, Passwort, Absender. **„Testmail senden“** prüft alles sofort
und nennt bei Fehlern die Ursache (Anmeldung, Verschlüsselung, Server nicht erreichbar …).

- Das Passwort wird mit ASP.NET Data Protection verschlüsselt gespeichert und nie wieder angezeigt. Die Schlüssel
  liegen in `data/files/.keys` und sind Teil der Sicherung (nach einer Wiederherstellung funktioniert das Konto weiter).
- Welche Benachrichtigungen verschickt werden (Freigaben, Helpdesk, Benutzerbrief per Mail), steht unter
  Einstellungen → Benachrichtigungen. Texte sind unter E-Mail → Vorlagen anpassbar (Platzhalter wie `{{name}}`).
- **Versandprotokoll:** jede Mail mit Empfänger, Betreff, Status, Fehlerursache; fehlgeschlagene werden automatisch
  wiederholt (1, 5, 30 Minuten, 2 Stunden) und lassen sich erneut senden. Inhalte werden nach dem Versand gelöscht.
- Für Entwickler: andere Module versenden über `IMailService` (`Modules/Mail/Exports/IMailService.cs`).
