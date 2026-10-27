#!/usr/bin/env bash
# Ende-zu-Ende-Test für Backup und Wiederherstellung (docs/backup.md): Vollsicherung mit Einstellungen, Liste der
# Sicherungen, Push an Admins, Status-Banner, Notfallblatt, Wiederherstellung aus dem Backup Center (ganzes System und
# einzelne Datei, mit Sperre), Störungsseite bei ausgefallener API, Totalverlust mit Dienst „restore“.
# Erwartet gebaute Images estrichone/api:$ESTRICHONE_VERSION und estrichone/backup:$ESTRICHONE_VERSION.
# Läuft im Verzeichnis deploy/ und räumt am Ende vollständig auf (data/, backup-repository/, .env).
set -euo pipefail
cd "$(dirname "$0")/.."

# Wie bei einer echten Installation: setup.sh muss die Datenverzeichnisse dem API-Benutzer übergeben können.
if [[ $EUID -ne 0 ]]; then
  echo "Bitte mit Root-Rechten ausführen: sudo -E $0" >&2
  exit 2
fi

export ESTRICHONE_VERSION="${ESTRICHONE_VERSION:-dev}"
PASSWORD="Estrich trocknet langsam 7"
CA="$(mktemp)"
CURL=(curl -sS --fail-with-body --noproxy localhost --cacert "$CA")

log() { printf '\n== %s\n' "$*"; }
cleanup() {
  local status=$?
  if [[ $status -ne 0 ]]; then
    echo "Fehlgeschlagen (Status $status). Letzte Protokollzeilen:" >&2
    docker compose logs --tail 60 api backup >&2 2>&1 || true
  fi
  docker compose --profile restore down -v >/dev/null 2>&1 || true
  rm -rf data backup-repository ssh .env "$CA"
}
trap cleanup EXIT

json() { python3 -c "import sys,json; print(json.load(sys.stdin)$1)"; }
api() { "${CURL[@]}" "$@"; }

log "Server einrichten"
cleanup
./setup.sh --domain localhost --name "Restore-Test" --yes >/dev/null
docker compose up -d --no-build --wait --wait-timeout 180 >/dev/null
docker compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt > "$CA"
EPOCH_BEFORE="$(api https://localhost/api/v1/server-info | json "['instanceEpoch']")"
CODE="$(docker compose logs api | grep -o 'Einrichtungscode: [A-Z0-9-]*' | tail -n 1 | awk '{print $2}')"
api -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/setup -d "{\"setupCode\":\"$CODE\",\"companyName\":\"Restore-Test\",\"administrator\":{\"displayName\":\"Admin\",\"userName\":\"admin\",\"email\":null,\"password\":\"$PASSWORD\"}}"
TOKEN="$(api -H 'Content-Type: application/json' https://localhost/api/v1/auth/login -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"test\"}" | json "['accessToken']")"
AUTH=(-H "Authorization: Bearer $TOKEN")

log "Daten anlegen: Bauakte und Foto"
BAUAKTE=01a15700-0000-7000-8000-00000000c0de
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/bauakten -d "{\"id\":\"$BAUAKTE\",\"title\":\"Wiederherstellung EFH\"}"
PHOTO="$(mktemp --suffix=.png)"
python3 - "$PHOTO" <<'PY'
import struct, sys, zlib
w, h = 32, 24
rows = b''.join(b'\x00' + b''.join(bytes([x * 7 % 256, y * 9 % 256, 99]) for x in range(w)) for y in range(h))
chunk = lambda t, d: struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
open(sys.argv[1], 'wb').write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b''))
PY
SHA="$(sha256sum "$PHOTO" | cut -d' ' -f1)"
FILE=01a15700-0000-7000-8000-00000000f11e
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/files -d "{\"id\":\"$FILE\",\"bauakteId\":\"$BAUAKTE\",\"kind\":\"photo\",\"originalName\":\"foto.png\",\"contentType\":\"image/png\",\"sizeBytes\":$(stat -c %s "$PHOTO"),\"sha256\":\"$SHA\"}"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/octet-stream' --data-binary @"$PHOTO" -o /dev/null "https://localhost/api/v1/files/$FILE/content"

log "Einstellungen setzen (Zeitplan, Aufbewahrung) – müssen nach jeder Wiederherstellung wieder da sein"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/backup/settings \
  -d '{"dailyTime":"21:45","incrementalEveryHours":6,"keepDaily":21,"keepWeekly":8,"keepMonthly":12,"keepYearly":3,"notifyAdmins":true,"notifyOnSuccess":true,"weeklyRestoreTest":true}'
grep -q '^BACKUP_TIME=21:45$' data/backup-status/settings.env || { echo "settings.env fehlt"; exit 1; }

log "Sichern, Integrität prüfen, Wiederherstellung testen"
docker compose exec -T backup estrichone-backup run
docker compose exec -T backup estrichone-backup check 100%
docker compose exec -T backup estrichone-backup restore-test
STATUS="$(api "${AUTH[@]}" https://localhost/api/v1/backup/status)"
[[ "$(json "['healthy']" <<<"$STATUS")" == "True" ]] || { echo "Backup Center meldet keinen gesunden Zustand: $STATUS"; exit 1; }
[[ "$(json "['lastRestoreTest']['status']" <<<"$STATUS")" == "succeeded" ]] || { echo "Wiederherstellungstest fehlt: $STATUS"; exit 1; }

log "Sicherungen im Backup Center, Push an Admins, kein Banner"
SNAPSHOTS="$(api "${AUTH[@]}" https://localhost/api/v1/backup/snapshots)"
SNAPSHOT="$(json "['repositories'][0]['snapshots'][0]['id']" <<<"$SNAPSHOTS")"
[[ "$(json "['repositories'][0]['snapshots'][0]['kind']" <<<"$SNAPSHOTS")" == "full" ]] || { echo "Keine Vollsicherung gelistet: $SNAPSHOTS"; exit 1; }
for _ in $(seq 1 30); do
  api "${AUTH[@]}" "https://localhost/api/v1/notifications" | grep -q 'Sicherung erfolgreich' && break
  sleep 5
done
api "${AUTH[@]}" "https://localhost/api/v1/notifications" | grep -q 'Sicherung erfolgreich' || { echo "Keine Pushnachricht an Admins nach der Sicherung"; exit 1; }
[[ "$(api "${AUTH[@]}" https://localhost/api/v1/system/health-banner | json "['banners']")" == "[]" ]] || { echo "Banner trotz frischer Sicherung"; exit 1; }
api "${AUTH[@]}" -o sheet.pdf https://localhost/api/v1/backup/emergency-sheet
[[ "$(head -c 5 sheet.pdf)" == "%PDF-" ]] || { echo "Notfallblatt ist kein PDF"; exit 1; }
rm -f sheet.pdf

log "Wiederherstellung aus dem Backup Center: Stand nach der Sicherung wird zurückgesetzt"
LATER=01a15700-0000-7000-8000-00000000beef
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/bauakten -d "{\"id\":\"$LATER\",\"title\":\"Nach der Sicherung\"}"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/backup/settings \
  -d '{"dailyTime":"03:00","incrementalEveryHours":0,"keepDaily":14,"keepWeekly":8,"keepMonthly":12,"keepYearly":3,"notifyAdmins":true,"notifyOnSuccess":true,"weeklyRestoreTest":true}'
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/backup/restore \
  -d "{\"snapshot\":\"$SNAPSHOT\",\"scope\":\"full\",\"confirmation\":\"WIEDERHERSTELLEN\"}"
SAW_LOCK=0
for _ in $(seq 1 90); do
  CODE_NOW="$(curl -s -o /dev/null -w '%{http_code}' --noproxy localhost --cacert "$CA" "${AUTH[@]}" https://localhost/api/v1/bauakten)"
  [[ "$CODE_NOW" == "503" ]] && SAW_LOCK=1
  STATE="$(api "${AUTH[@]}" https://localhost/api/v1/backup/restore | json "['status']" || true)"
  [[ "$STATE" == "completed" || "$STATE" == "failed" ]] && break
  sleep 2
done
[[ "$STATE" == "completed" ]] || { echo "Wiederherstellung nicht abgeschlossen: $STATE"; docker compose logs --tail 40 backup; exit 1; }
[[ $SAW_LOCK -eq 1 ]] || echo "Hinweis: Sperre (503) während der Wiederherstellung nicht beobachtet (zu schnell)."
TOKEN="$(api -H 'Content-Type: application/json' https://localhost/api/v1/auth/login -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"test\"}" | json "['accessToken']")"
AUTH=(-H "Authorization: Bearer $TOKEN")
[[ "$(curl -s -o /dev/null -w '%{http_code}' --noproxy localhost --cacert "$CA" "${AUTH[@]}" "https://localhost/api/v1/bauakten/$LATER")" == "404" ]] \
  || { echo "Bauakte nach der Sicherung ist nach der Wiederherstellung noch da"; exit 1; }
[[ "$(api "${AUTH[@]}" "https://localhost/api/v1/bauakten/$BAUAKTE" | json "['title']")" == "Wiederherstellung EFH" ]] || { echo "Bauakte fehlt"; exit 1; }
[[ "$(api "${AUTH[@]}" https://localhost/api/v1/backup/settings | json "['dailyTime']")" == "21:45" ]] || { echo "Einstellungen nicht wiederhergestellt"; exit 1; }
[[ "$(api "${AUTH[@]}" "https://localhost/api/v1/files/$FILE/content" | sha256sum | cut -d' ' -f1)" == "$SHA" ]] || { echo "Foto verändert"; exit 1; }

log "Einzelne Datei wiederherstellen (Original gelöscht)"
find data/files/originals -name "${FILE//-/}" -delete
[[ "$(curl -s -o /dev/null -w '%{http_code}' --noproxy localhost --cacert "$CA" "${AUTH[@]}" "https://localhost/api/v1/files/$FILE/content")" != "200" ]] \
  || { echo "Original wurde nicht gelöscht"; exit 1; }
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null https://localhost/api/v1/backup/restore \
  -d "{\"snapshot\":\"$SNAPSHOT\",\"scope\":\"file\",\"fileId\":\"$FILE\"}"
for _ in $(seq 1 45); do
  STATE="$(api "${AUTH[@]}" https://localhost/api/v1/backup/restore | json "['status']" || true)"
  [[ "$STATE" == "completed" || "$STATE" == "failed" ]] && break
  sleep 2
done
[[ "$STATE" == "completed" ]] || { echo "Datei-Wiederherstellung: $STATE"; exit 1; }
[[ "$(api "${AUTH[@]}" "https://localhost/api/v1/files/$FILE/content" | sha256sum | cut -d' ' -f1)" == "$SHA" ]] || { echo "Datei nicht zurück"; exit 1; }

log "Störungsseite, wenn die API nicht läuft"
docker compose stop api >/dev/null
BODY="$(curl -s --noproxy localhost --cacert "$CA" https://localhost/api/v1/server-info)"
grep -q '"code":"SERVER_UNAVAILABLE"' <<<"$BODY" || { echo "Kein Problem-JSON bei ausgefallener API: $BODY"; exit 1; }
curl -sf --noproxy localhost --cacert "$CA" https://localhost/stoerung.html | grep -q 'EstrichONE ist gerade nicht erreichbar' || { echo "Störungsseite fehlt"; exit 1; }
docker compose start api >/dev/null

log "Totalverlust simulieren: Datenbank und Dateien löschen"
docker compose down >/dev/null
rm -rf data/postgres data/files data/backup-status
./setup.sh --yes >/dev/null   # legt leere Verzeichnisse an; .env und Backup-Repository bleiben

log "Wiederherstellen"
docker compose up -d --no-build --wait postgres >/dev/null
docker compose --profile restore run --rm restore latest
docker compose up -d --no-build --wait --wait-timeout 180 >/dev/null

log "Prüfen"
TOKEN="$(api -H 'Content-Type: application/json' https://localhost/api/v1/auth/login -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"test\"}" | json "['accessToken']")"
AUTH=(-H "Authorization: Bearer $TOKEN")
TITLE="$(api "${AUTH[@]}" "https://localhost/api/v1/bauakten/$BAUAKTE" | json "['title']")"
[[ "$TITLE" == "Wiederherstellung EFH" ]] || { echo "Bauakte fehlt nach Wiederherstellung"; exit 1; }
RESTORED_SHA="$(api "${AUTH[@]}" "https://localhost/api/v1/files/$FILE/content" | sha256sum | cut -d' ' -f1)"
[[ "$RESTORED_SHA" == "$SHA" ]] || { echo "Foto nach Wiederherstellung verändert oder fehlend"; exit 1; }
EPOCH_AFTER="$(api https://localhost/api/v1/server-info | json "['instanceEpoch']")"
[[ "$EPOCH_AFTER" != "$EPOCH_BEFORE" ]] || { echo "instanceEpoch wurde nicht erneuert"; exit 1; }

[[ "$(api "${AUTH[@]}" https://localhost/api/v1/backup/settings | json "['dailyTime']")" == "21:45" ]] || { echo "Einstellungen fehlen nach Totalverlust"; exit 1; }

log "Backup und Wiederherstellung erfolgreich: Backup Center (System, Datei), Einstellungen, Bauakte, Foto (SHA-256 identisch), Anmeldung, neuer instanceEpoch, Störungsseite"
