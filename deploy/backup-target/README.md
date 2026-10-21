# EstrichONE-Backup-Ziel – Sicherung auf einem zweiten Rechner

Ein Backup-Ziel ist ein zweiter Rechner (NAS mit Docker, alter PC, Raspberry Pi mit großer USB-Platte), auf den der
EstrichONE-Server **zusätzlich** jede Nacht sichert. Das Ziel nimmt nur neue Sicherungen an und **lässt nichts
löschen** („nur anhängen“). Selbst ein Verschlüsselungstrojaner auf dem EstrichONE-Server kann die Sicherungen dort
nicht zerstören. Die Sicherungen sind verschlüsselt; der Zielrechner selbst kann sie nicht lesen.

## In 3 Schritten

**1. Auf dem Zielrechner** (Linux mit Docker):

```bash
sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest backup-ziel
```

Er zeigt **Adresse** (z. B. `192.168.1.60:8444`) und **Kopplungscode** an. Eigener Ordner für die Sicherungen (z. B.
die große Platte): zusätzlich `-e ESTRICHONE_DATA_DIR=/mnt/usb/estrichone`.

**2. In EstrichONE:** System → **Backup-Ziele** → „Backup-Ziel koppeln“ → Adresse und Code eingeben.

**3. „Notfallzugang“ herunterladen** und zusammen mit dem Backup-Passwort (steht in `/opt/estrichone/.env` unter
`RESTIC_PASSWORD`) im Passwortmanager oder ausgedruckt im Tresor aufbewahren.

Ab der nächsten Sicherung (täglich 22:30 Uhr oder System → Sicherung → „Jetzt sichern“) geht jede Sicherung auch
auf das Ziel. Den Stand zeigt System → Backup-Ziele: letzte Sicherung dorthin, Anzahl Sicherungsstände, belegter und
freier Platz.

## Wiederherstellen nach Totalverlust des Servers

Im Notfallzugang steht die genaue Anleitung. Kurz: EstrichONE neu installieren, alte `.env` zurückkopieren, das
Zertifikat aus dem Notfallzugang als `/opt/estrichone/ssh/backup-ziel-ca.pem` speichern, dann

```bash
cd /opt/estrichone && sudo docker compose -p estrichone stop api
sudo docker compose -p estrichone --profile restore run --rm \
  -e RESTORE_FROM='rest:https://…(aus dem Notfallzugang)…' \
  -e RESTIC_CACERT=/home/backup/.ssh/backup-ziel-ca.pem restore latest
sudo docker compose -p estrichone up -d
```

## Gut zu wissen

- **Speicherplatz:** Weil das Ziel nichts löschen lässt, wächst es langsam (nur Änderungen werden übertragen,
  Fotos also nur einmal). Wird es voll: auf dem Zielrechner ein weiteres Backup-Ziel (neue Platte) einrichten und
  koppeln, das alte abkoppeln und aufbewahren.
- **Aktualisieren:** Installationsbefehl erneut ausführen; Sicherungen und Kopplung bleiben.
- **Protokoll:** `sudo docker logs estrichone-backup-target`
- **Neuer Kopplungscode:** `sudo docker exec estrichone-backup-target estrichone-archive pair`
- Port **8444** (Standard des Installers; der Archivserver nutzt 8443) muss vom EstrichONE-Server aus erreichbar sein. Kein Port-Forwarding ins Internet nötig.

## Technik

Image `ghcr.io/florians25/estrichone-backup-target`: der bewährte **restic REST server** (0.14, `--append-only`,
`--private-repos`, nur auf 127.0.0.1) hinter demselben kleinen .NET-Dienst wie der Archivserver. Dieser macht HTTPS mit
eigenem Zertifikat, Kopplung per Code (Zertifikat-Pinning, HMAC-Beweis) und prüft die Zugangsdaten je gekoppeltem
Server; jeder Server hat sein eigenes Repository. EstrichONE legt Zugang und Zertifikat für den Backup-Dienst unter
`data/backup-status/targets/` ab; der Backup-Dienst nutzt sie mit `restic --cacert`.
