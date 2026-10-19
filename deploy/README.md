# EstrichONE – Installation und Betrieb

## Voraussetzungen

- Linux-Server (z. B. Ubuntu 24.04 oder Debian 12), mind. 2 vCPU, 4 GB RAM, SSD
- Docker Engine ≥ 24 mit Compose-Plugin
- Für den Zugriff aus dem Internet bzw. von Smartphones: eine Domain, die auf den Server zeigt,
  und offene Ports 80 und 443

## Installation

```bash
git clone https://github.com/florians25/EstrichONE.git estrichone
cd estrichone/deploy
sudo ./setup.sh                      # fragt Adresse und Firmennamen ab, erzeugt .env mit sicherem Passwort
sudo docker compose up -d --build    # baut und startet Datenbank, Migration, API und Reverse Proxy
curl https://<ihre-adresse>/api/v1/server-info
```

## Ersteinrichtung (erster Administrator)

Solange es keinen Benutzer gibt, schreibt der Server beim Start einen **Einrichtungscode** ins Protokoll
(24 Stunden gültig, einmalig; jeder Neustart erzeugt einen neuen Code):

```bash
sudo docker compose logs api | grep Einrichtungscode
```

Mit diesem Code werden Firmenname und erster Administrator angelegt – bis die Weboberfläche fertig ist per API:

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
| `caddy` | HTTPS, Zertifikate, Weiterleitung an die API | ja (80, 443) |
| `api` | EstrichONE-Server | nein |
| `migrate` | aktualisiert das Datenbankschema bei jedem Start und beendet sich dann | nein |
| `postgres` | Datenbank | nein (internes Netz ohne Internetzugang) |

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

## Aktualisieren

```bash
git pull
docker compose up -d --build          # migrate aktualisiert das Schema automatisch vor dem API-Start
```

Ein Update-Skript mit vorherigem Backup folgt mit dem Backup Center (Arbeitspaket AP-09).
Bis dahin: **vor jedem Update `data/` sichern.**
