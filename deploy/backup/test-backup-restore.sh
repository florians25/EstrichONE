#!/usr/bin/env bash
# Ende-zu-Ende-Test für Backup und Wiederherstellung (docs/reengineering/16, AK-1).
# Erwartet gebaute Images estrichone/api:$ESTRICHONE_VERSION und estrichone/backup:$ESTRICHONE_VERSION.
# Läuft im Verzeichnis deploy/ und räumt am Ende vollständig auf (data/, backup-repository/, .env).
set -euo pipefail
cd "$(dirname "$0")/.."

export ESTRICHONE_VERSION="${ESTRICHONE_VERSION:-dev}"
PASSWORD="Estrich trocknet langsam 7"
CA="$(mktemp)"
CURL=(curl -sS --fail-with-body --noproxy localhost --cacert "$CA")

log() { printf '\n== %s\n' "$*"; }
cleanup() {
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

log "Sichern, Integrität prüfen, Wiederherstellung testen"
docker compose exec -T backup estrichone-backup run
docker compose exec -T backup estrichone-backup check 100%
docker compose exec -T backup estrichone-backup restore-test
STATUS="$(api "${AUTH[@]}" https://localhost/api/v1/backup/status)"
[[ "$(json "['healthy']" <<<"$STATUS")" == "True" ]] || { echo "Backup Center meldet keinen gesunden Zustand: $STATUS"; exit 1; }
[[ "$(json "['lastRestoreTest']['status']" <<<"$STATUS")" == "succeeded" ]] || { echo "Wiederherstellungstest fehlt: $STATUS"; exit 1; }

log "Totalverlust simulieren: Datenbank und Dateien löschen"
docker compose down >/dev/null
rm -rf data/postgres data/files
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

log "Backup und Wiederherstellung erfolgreich: Bauakte, Foto (SHA-256 identisch), Anmeldung, neuer instanceEpoch"
