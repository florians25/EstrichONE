#!/usr/bin/env bash
# Worst Case Ende zu Ende (docs/backup.md, „Server komplett ausgefallen“): Installation per Installer → Bauakte, Foto und
# Einstellungen → Sicherung → Kopie des Repositorys auf „USB-Platte“ → Server samt /opt/estrichone gelöscht →
# frischer Server mit EINEM Befehl (Installer-Modus „restore“, Schlüssel wie vom Notfallblatt mit Leerzeichen) → alles da.
# Erwartet die Images ghcr.io/florians25/estrichone-{installer,api,backup,web}:$VERSION (lokal gebaut). Räumt nicht ab.
# shellcheck disable=SC1111,SC1112
set -euo pipefail

VERSION="${VERSION:?VERSION fehlt}"
INSTALLER="ghcr.io/florians25/estrichone-installer:$VERSION"
DIR=/opt/estrichone
URL=http://localhost:8181
USB=/tmp/estrichone-usb-backup
PASSWORD="Estrich trocknet langsam 7"
[[ $EUID -eq 0 ]] || { echo "Bitte mit Root-Rechten ausführen: sudo -E $0" >&2; exit 2; }

log() { printf '\n== %s\n' "$*"; }
json() { python3 -c "import sys,json; print(json.load(sys.stdin)$1)"; }
api() { curl -sS --fail-with-body --noproxy localhost "$@"; }
compose() { (cd "$DIR" && docker compose -p estrichone "$@"); }
login() {
  api -H 'Content-Type: application/json' "$URL/api/v1/auth/login" \
    -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"test\"}" | json "['accessToken']"
}
on_exit() {
  local status=$?
  [[ $status -eq 0 ]] || { echo "Fehlgeschlagen ($status)"; compose ps; compose logs --tail 60 api backup; }
}
trap on_exit EXIT

if [[ ! -f "$DIR/.env" ]]; then
  log "Installieren"
  mkdir -p "$DIR"
  docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$DIR:$DIR" -e ESTRICHONE_MODE=proxy -e ESTRICHONE_DOMAIN=localhost \
    -e ESTRICHONE_NAME="Worst Case GmbH" "$INSTALLER" >/dev/null
fi
for _ in $(seq 1 60); do api -o /dev/null "$URL/api/v1/server-info" 2>/dev/null && break; sleep 2; done

log "Ersteinrichtung bzw. Anmeldung"
if api "$URL/api/v1/server-info" | grep -q '"setupRequired":true'; then
  CODE="$(compose logs api | grep -o 'Einrichtungscode: [A-Z0-9-]*' | tail -n 1 | awk '{print $2}')"
  api -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/setup" \
    -d "{\"setupCode\":\"$CODE\",\"companyName\":\"Worst Case GmbH\",\"administrator\":{\"displayName\":\"Admin\",\"userName\":\"admin\",\"email\":null,\"password\":\"$PASSWORD\"}}"
fi
AUTH=(-H "Authorization: Bearer $(login)")
EPOCH_BEFORE="$(api "$URL/api/v1/server-info" | json "['instanceEpoch']")"

log "Daten: Bauakte, Foto, Einstellungen"
BAUAKTE=01a15700-0000-7000-8000-00000000c001
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/bauakten" -d "{\"id\":\"$BAUAKTE\",\"title\":\"Neubau nach Totalverlust\"}"
PHOTO="$(mktemp --suffix=.png)"
python3 - "$PHOTO" <<'PY'
import struct, sys, zlib
w, h = 40, 30
rows = b''.join(b'\x00' + b''.join(bytes([x * 5 % 256, y * 11 % 256, 42]) for x in range(w)) for y in range(h))
chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
open(sys.argv[1], 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b''))
PY
SHA="$(sha256sum "$PHOTO" | cut -d' ' -f1)"
FILE=01a15700-0000-7000-8000-00000000f001
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/files" \
  -d "{\"id\":\"$FILE\",\"bauakteId\":\"$BAUAKTE\",\"kind\":\"photo\",\"originalName\":\"estrich.png\",\"contentType\":\"image/png\",\"sizeBytes\":$(stat -c %s "$PHOTO"),\"sha256\":\"$SHA\"}"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/octet-stream' --data-binary @"$PHOTO" -o /dev/null "$URL/api/v1/files/$FILE/content"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/backup/settings" \
  -d '{"dailyTime":"20:15","incrementalEveryHours":4,"keepDaily":30,"keepWeekly":8,"keepMonthly":12,"keepYearly":3,"notifyAdmins":true,"notifyOnSuccess":false,"weeklyRestoreTest":true}'

log "Sichern und Kopie auf die „USB-Platte“"
compose exec -T backup estrichone-backup run
rm -rf "$USB" && cp -a "$DIR/backup-repository" "$USB"
KEY="$(sed -n 's/^RESTIC_PASSWORD=//p' "$DIR/.env")"
NAME_LINE="$(grep '^ESTRICHONE_SERVER_NAME=' "$DIR/.env")"
# So wie vom Notfallblatt abgeschrieben: in Viererblöcken
GROUPED="$(sed 's/.\{4\}/& /g; s/ $//' <<<"$KEY")"

log "Totalverlust: Dienste, Daten und Installationsverzeichnis weg"
compose --profile restore down -v --remove-orphans >/dev/null 2>&1
rm -rf "$DIR" && mkdir -p "$DIR"

log "Neuinstallation mit einem Befehl: Installer-Modus restore"
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$DIR:$DIR" \
  -e ESTRICHONE_RESTORE_FROM="$USB" -e ESTRICHONE_BACKUP_PASSWORD="$GROUPED" "$INSTALLER" restore | tee /tmp/estrichone-restore.log
grep -q "Wiederherstellung abgeschlossen" /tmp/estrichone-restore.log
for _ in $(seq 1 60); do api -o /dev/null "$URL/api/v1/server-info" 2>/dev/null && break; sleep 2; done

log "Prüfen: Anmeldung, Bauakte, Foto, Einstellungen, Konfiguration, neue Instanz-Kennung"
AUTH=(-H "Authorization: Bearer $(login)")
[[ "$(api "${AUTH[@]}" "$URL/api/v1/bauakten/$BAUAKTE" | json "['title']")" == "Neubau nach Totalverlust" ]] || { echo "Bauakte fehlt"; exit 1; }
[[ "$(api "${AUTH[@]}" "$URL/api/v1/files/$FILE/content" | sha256sum | cut -d' ' -f1)" == "$SHA" ]] || { echo "Foto fehlt oder verändert"; exit 1; }
SETTINGS="$(api "${AUTH[@]}" "$URL/api/v1/backup/settings")"
[[ "$(json "['dailyTime']" <<<"$SETTINGS")" == "20:15" && "$(json "['keepDaily']" <<<"$SETTINGS")" == "30" ]] || { echo "Einstellungen fehlen: $SETTINGS"; exit 1; }
grep -qxF "$NAME_LINE" "$DIR/.env" || { echo ".env nicht aus der Sicherung"; exit 1; }
[[ "$(sed -n 's/^RESTIC_PASSWORD=//p' "$DIR/.env")" == "$KEY" ]] || { echo "Backup-Passwort nicht übernommen"; exit 1; }
[[ "$(api "$URL/api/v1/server-info" | json "['instanceEpoch']")" != "$EPOCH_BEFORE" ]] || { echo "instanceEpoch unverändert"; exit 1; }
compose exec -T backup estrichone-backup run >/dev/null

log "Backup-Ziel, das zieht (zuhause hinter NAT): Code im Backup Center, ein Befehl, Spiegel ist wiederherstellbar"
MIRROR="$(api "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/backup/mirrors" -d '{"name":"NAS zuhause"}')"
MCODE="$(json "['code']" <<<"$MIRROR")"
docker rm -f estrichone-backup-target >/dev/null 2>&1 || true
docker volume rm estrichone-backup-target >/dev/null 2>&1 || true
# In der CI erreicht das Ziel die API direkt im Docker-Netz; im echten Betrieb ist es die öffentliche HTTPS-Adresse.
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -e ESTRICHONE_PULL_FROM=http://api:8080 -e ESTRICHONE_PULL_CODE="$MCODE" \
  -e ESTRICHONE_NETWORK=estrichone_frontend -e ESTRICHONE_NAME="Keller" "$INSTALLER" backup-ziel | tee /tmp/estrichone-mirror.log
grep -q "ZIEHT die Sicherungen" /tmp/estrichone-mirror.log
mirrored() { python3 -c "import sys,json; r=json.load(sys.stdin)['mirrors'][0].get('lastReport') or {}; print(r.get('snapshots', 0))"; }
for _ in $(seq 1 60); do
  STATE="$(api "${AUTH[@]}" "$URL/api/v1/backup/mirrors")"
  [[ "$(mirrored <<<"$STATE")" -ge 1 ]] && break
  sleep 3
done
echo "$STATE"
[[ "$(json "['mirrors'][0]['reachable']" <<<"$STATE")" == "True" && "$(mirrored <<<"$STATE")" -ge 1 ]] \
  || { echo "Spiegel unvollständig"; docker logs estrichone-backup-target 2>&1 | tail -20; exit 1; }
docker run --rm -v estrichone-backup-target:/ziel:ro -e RESTIC_PASSWORD="$KEY" -e RESTIC_CACHE_DIR=/tmp/c --entrypoint restic \
  "ghcr.io/florians25/estrichone-backup:$VERSION" --no-lock -r /ziel/mirror check --read-data
docker rm -f estrichone-backup-target >/dev/null
docker volume rm estrichone-backup-target >/dev/null
rm -f "$PHOTO"
log "Worst Case erfolgreich: neuer Server aus der Sicherung (Daten, Foto, Einstellungen, Konfiguration, Anmeldung); Spiegel zuhause vollständig"
