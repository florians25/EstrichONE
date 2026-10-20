# EstrichONE – Installation und Betrieb

## Voraussetzungen

- Linux-Server (z. B. Ubuntu 24.04 oder Debian 12), mind. 2 vCPU, 4 GB RAM, SSD
- Docker Engine ≥ 24 mit Compose-Plugin
- Für den Zugriff aus dem Internet bzw. von Smartphones: eine Domain, die auf den Server zeigt,
  und offene Ports 80 und 443

## Installation mit einem Docker-Befehl (empfohlen zum Testen)

```bash
sudo docker run --rm -it \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v /opt/estrichone:/opt/estrichone \
  ghcr.io/florians25/estrichone-installer:latest
```

Der Installer fragt Adresse (Domain oder IP) und Firmennamen ab, legt `/opt/estrichone` mit `.env` (zufällige
Passwörter) an, lädt die fertigen Images und startet die Suite. Er zeigt am Ende das **Backup-Passwort** und den
**Einrichtungscode** für den ersten Administrator.

| Port | Zweck |
|---|---|
| **8143** | HTTPS – Weboberfläche und API (`https://<adresse>:8143`) |
| **8180** | HTTP – leitet auf 8143 weiter |

- Ohne Rückfragen: `-e ESTRICHONE_DOMAIN=estrichone.firma.de -e ESTRICHONE_NAME="Estrichbau Muster GmbH"`
- Andere Ports: `-e ESTRICHONE_HTTP_PORT=8180 -e ESTRICHONE_HTTPS_PORT=8143`
- **Aktualisieren:** denselben Befehl erneut ausführen (Daten und Konfiguration bleiben, Version wird angehoben).
- Bei IP-Adresse oder `localhost` stellt Caddy ein eigenes Zertifikat aus (der Browser warnt einmal; die Android-App
  akzeptiert es nicht). Für ein gültiges Let's-Encrypt-Zertifikat eine Domain verwenden und am Router
  **80 → 8180** und **443 → 8143** weiterleiten.
- Verwalten: `cd /opt/estrichone && sudo docker compose -p estrichone ps|logs|down`

## Installation aus dem Quellcode

```bash
git clone https://github.com/florians25/EstrichONE.git estrichone
cd estrichone/deploy
sudo ./setup.sh                      # fragt Adresse und Firmennamen ab, erzeugt .env mit sicherem Passwort
sudo docker compose up -d --build    # baut und startet Datenbank, Migration, API, Weboberfläche und Reverse Proxy
curl https://<ihre-adresse>/api/v1/server-info
```

## Ersteinrichtung (erster Administrator)

Solange es keinen Benutzer gibt, schreibt der Server beim Start einen **Einrichtungscode** ins Protokoll
(24 Stunden gültig, einmalig; jeder Neustart erzeugt einen neuen Code):

```bash
sudo docker compose logs api | grep Einrichtungscode
```

Im Browser `https://<ihre-adresse>/` öffnen, „Jetzt einrichten“ wählen und mit diesem Code Firmenname und
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
| `api` | EstrichONE-Server | nein |
| `migrate` | aktualisiert das Datenbankschema bei jedem Start und beendet sich dann | nein |
| `postgres` | Datenbank | nein (internes Netz ohne Internetzugang) |
| `backup` | tägliche verschlüsselte Sicherung (restic), Prüfung, Wiederherstellungstest | nein |
| `restore` | nur bei Bedarf: Wiederherstellung (Profil `restore`) | nein |

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
docker compose ps                     # alle Dienste „healthy“, migrate „Exited (0)“
docker compose logs -f api            # Protokoll (JSON, ohne personenbezogene Inhalte)
curl https://<adresse>/health/ready   # 200 = API und Datenbank bereit
```

## Sicherung (Backup)

Der Dienst `backup` sichert **täglich um 22:30** (`BACKUP_TIME`) Datenbank und Dateien verschlüsselt mit restic.
Sonntags kommen dazu: Aufbewahrungsregeln anwenden, Integrität prüfen und **eine echte Wiederherstellung in eine
Testdatenbank**. Den Zustand zeigt `GET /api/v1/backup/status` (später das Backup Center). `/health/ready` meldet
„Degraded“, wenn die letzte erfolgreiche Sicherung älter als 36 Stunden ist.

| Einstellung (.env) | Bedeutung |
|---|---|
| `RESTIC_PASSWORD` | Schlüssel der Sicherungen – **von setup.sh angezeigt, außerhalb des Servers aufbewahren** |
| `ESTRICHONE_BACKUP_DIR` | Hauptziel auf dem Host, idealerweise zweite Festplatte oder eingebundenes NAS |
| `BACKUP_REPOSITORY_2` | **dringend empfohlen:** zweites Ziel an anderem Ort, z. B. `sftp:backup@nas2.firma.de:/estrichone` (Schlüssel in `ESTRICHONE_SSH_DIR`) |

Gesichert werden Datenbank und Dateien. **Nicht** gesichert wird die `.env` (Passwörter). Sie gehört getrennt in
einen Passwortmanager.

```bash
docker compose exec backup estrichone-backup run           # jetzt sichern
docker compose exec backup estrichone-backup snapshots     # Sicherungen anzeigen
docker compose exec backup estrichone-backup check 100%    # alles vollständig prüfen
```

## Wiederherstellen (auch auf einem neuen Server)

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

```bash
docker compose exec backup estrichone-backup run   # vorher sichern
git pull
docker compose up -d --build                        # migrate aktualisiert das Schema vor dem API-Start
```
