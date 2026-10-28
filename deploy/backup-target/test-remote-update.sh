#!/usr/bin/env bash
# Fernaktualisierung der Zusatzserver Ende zu Ende (docs/backup.md, „Zusatzserver aktualisieren“), gegen den laufenden
# Stack aus test-installer-restore.sh (Benutzer admin):
#   1. Backup-Ziel mit der alten Test-Version $OLD_VERSION per Installer einrichten (mit Updater) und koppeln, sichern.
#   2. Ungültige bzw. zu neue Versionen lehnt der Server ab.
#   3. Fernupdate auf $BROKEN_VERSION (Container wird nie gesund) → automatischer Rücksprung auf $OLD_VERSION.
#   4. Fernupdate auf die Version des Servers → gesund, Kopplung und Sicherungen erhalten, erneute Sicherung klappt.
#   5. Backup-Ziel, das zieht: Auftrag wird beim Melden abgeholt und ausgeführt.
# Erwartet lokal gebaute Images ghcr.io/florians25/estrichone-{installer,backup-target}:{$VERSION,$OLD_VERSION,$BROKEN_VERSION}.
# shellcheck disable=SC1111,SC1112
set -euo pipefail

VERSION="${VERSION:?VERSION fehlt}"
OLD="${OLD_VERSION:?OLD_VERSION fehlt}"
BROKEN="${BROKEN_VERSION:?BROKEN_VERSION fehlt}"
REGISTRY=ghcr.io/florians25
DIR=/opt/estrichone
URL=http://localhost:8181
NET=estrichone_frontend
PASSWORD="Estrich trocknet langsam 7"
NAME=estrichone-backup-target
[[ $EUID -eq 0 ]] || { echo "Bitte mit Root-Rechten ausführen: sudo -E $0" >&2; exit 2; }

log() { printf '\n== %s\n' "$*"; }
json() { python3 -c "import sys,json; d=json.load(sys.stdin); print(eval('d'+sys.argv[1]))" "$1"; }
api() { curl -sS --fail-with-body --noproxy localhost "$@"; }
compose() { (cd "$DIR" && docker compose -p estrichone "$@"); }
cleanup_peer() {
  docker rm -f "$NAME" "$NAME-updater" "$NAME-updatejob" "$NAME-vorher" >/dev/null 2>&1 || true
  docker volume rm "$NAME" "$NAME-update" >/dev/null 2>&1 || true
}
on_exit() {
  local status=$?
  if [[ $status -ne 0 ]]; then
    echo "Fehlgeschlagen ($status)"
    docker ps -a --format '{{.Names}} {{.Image}} {{.Status}}' | grep "$NAME" || true
    for c in "$NAME" "$NAME-updater" "$NAME-updatejob"; do echo "--- $c"; docker logs --tail 30 "$c" 2>&1 || true; done
    compose logs --tail 40 api
  fi
}
trap on_exit EXIT

AUTH=(-H "Authorization: Bearer $(api -H 'Content-Type: application/json' "$URL/api/v1/auth/login" \
  -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"test\"}" | json "['accessToken']")")
item() { # item <key> → JSON des Eintrags
  api "${AUTH[@]}" "$URL/api/v1/backup/peer-updates" \
    | python3 -c "import sys,json; print(json.dumps(next(i for i in json.load(sys.stdin)['items'] if i['key']==sys.argv[1])))" "$1"
}
field() { item "$1" | json "['$2']"; }
request() { # request <key> <json> → HTTP-Status
  curl -sS --noproxy localhost -o /tmp/peer-update.json -w '%{http_code}' "${AUTH[@]}" -H 'Content-Type: application/json' \
    "$URL/api/v1/backup/peer-updates/$1/update" -d "$2"
}
wait_done() { # wait_done <key> → Endstatus
  local status=""
  for _ in $(seq 1 100); do
    status="$(field "$1" status 2>/dev/null || true)"
    case "$status" in succeeded|rolled_back|failed) break ;; esac
    sleep 3
  done
  echo "$status"
}
image_of() { docker container inspect -f '{{.Config.Image}}' "$1"; }
healthy() { [[ "$(docker container inspect -f '{{.State.Health.Status}}' "$NAME")" == healthy ]] || docker exec "$NAME" estrichone-archive healthcheck; }

log "Backup-Ziel $OLD einrichten (mit Updater) und koppeln"
cleanup_peer
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -e ESTRICHONE_NETWORK="$NET" -e ESTRICHONE_NAME="Ziel Keller" \
  "$REGISTRY/estrichone-installer:$OLD" backup-ziel | tee /tmp/peer-install.log
grep -q "Fernaktualisierung: an" /tmp/peer-install.log
[[ "$(image_of "$NAME-updater")" == "$REGISTRY/estrichone-installer:$OLD" ]]
CODE="$(sed -n 's/.*Kopplungscode: *\([A-Z0-9-]*\).*/\1/p' /tmp/peer-install.log)"
IP="$(docker inspect -f "{{(index .NetworkSettings.Networks \"$NET\").IPAddress}}" "$NAME")"
TARGET="$(api "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/backup/targets" -d "{\"address\":\"$IP:8443\",\"code\":\"$CODE\"}")"
TARGET_ID="$(json "['id']" <<<"$TARGET")"
KEY="target-${TARGET_ID//-/}"
compose exec -T backup estrichone-backup run >/dev/null
snapshots() { api "${AUTH[@]}" "$URL/api/v1/backup/targets" | json "[0]['remote']['snapshots']"; }
BEFORE="$(snapshots)"
[[ "$BEFORE" -ge 1 ]] || { echo "Keine Sicherung auf dem Ziel"; exit 1; }
ITEM="$(item "$KEY")"
echo "$ITEM"
[[ "$(json "['installedVersion']" <<<"$ITEM")" == "$OLD" && "$(json "['availableVersion']" <<<"$ITEM")" == "$VERSION" ]]
[[ "$(json "['updateAvailable']" <<<"$ITEM")" == True && "$(json "['updaterAvailable']" <<<"$ITEM")" == True ]]

log "Ungültige und zu neue Versionen werden abgelehnt (keine beliebigen Images)"
[[ "$(request "$KEY" '{"version":"9.9.9"}')" == 422 ]]
grep -q PEER_UPDATE_VERSION_TOO_NEW /tmp/peer-update.json
[[ "$(request "$KEY" '{"version":"latest"}')" == 422 ]]
grep -q PEER_UPDATE_VERSION_INVALID /tmp/peer-update.json
[[ "$(request "$KEY" '{"version":"1.0.0 --privileged"}')" == 422 ]]

log "Rücksprung: $BROKEN wird nicht gesund → $OLD läuft wieder"
[[ "$(request "$KEY" "{\"version\":\"$BROKEN\"}")" == 202 ]] || { cat /tmp/peer-update.json; exit 1; }
[[ "$(wait_done "$KEY")" == rolled_back ]] || { item "$KEY"; exit 1; }
item "$KEY"
[[ "$(image_of "$NAME")" == "$REGISTRY/estrichone-backup-target:$OLD" ]]
healthy
[[ "$(field "$KEY" installedVersion)" == "$OLD" ]]
if docker container inspect "$NAME-vorher" >/dev/null 2>&1; then echo "Vorige Version nicht aufgeräumt"; exit 1; fi

log "Fernupdate auf die Version des Servers ($VERSION)"
for _ in $(seq 1 20); do [[ "$(field "$KEY" updaterAvailable)" == True ]] && break; sleep 3; done
[[ "$(request "$KEY" '{}')" == 202 ]] || { cat /tmp/peer-update.json; exit 1; }
[[ "$(wait_done "$KEY")" == succeeded ]] || { item "$KEY"; exit 1; }
item "$KEY"
[[ "$(image_of "$NAME")" == "$REGISTRY/estrichone-backup-target:$VERSION" ]]
[[ "$(image_of "$NAME-updater")" == "$REGISTRY/estrichone-installer:$VERSION" ]]
healthy
[[ "$(field "$KEY" installedVersion)" == "$VERSION" && "$(field "$KEY" updateAvailable)" == False ]]
log "Daten erhalten: Kopplung, Sicherungen, erneute Sicherung"
[[ "$(snapshots)" == "$BEFORE" ]] || { echo "Sicherungen auf dem Ziel verändert"; exit 1; }
compose exec -T backup estrichone-backup run >/dev/null
[[ "$(snapshots)" -gt "$BEFORE" ]] || { echo "Sicherung nach dem Update fehlt"; exit 1; }
NOTES="$(api "${AUTH[@]}" "$URL/api/v1/notifications?limit=50")"
if ! grep -q "Aktualisierung zurückgerollt" <<<"$NOTES" || ! grep -q "Zusatzserver aktualisiert" <<<"$NOTES"; then
  echo "Benachrichtigungen fehlen: $NOTES"; exit 1
fi
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/backup/targets/$TARGET_ID"
cleanup_peer

log "Backup-Ziel, das zieht ($OLD): Auftrag wird beim Melden abgeholt"
MIRROR="$(api "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/backup/mirrors" -d '{"name":"NAS Fernupdate"}')"
MKEY="mirror-$(json "['mirror']['id']" <<<"$MIRROR")"
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -e ESTRICHONE_PULL_FROM=http://api:8080 \
  -e ESTRICHONE_PULL_CODE="$(json "['code']" <<<"$MIRROR")" -e ESTRICHONE_NETWORK="$NET" -e ESTRICHONE_CHECKIN_SECONDS=10 \
  "$REGISTRY/estrichone-installer:$OLD" backup-ziel >/dev/null
for _ in $(seq 1 40); do
  [[ "$(field "$MKEY" updaterAvailable 2>/dev/null || true)" == True ]] && break
  sleep 3
done
item "$MKEY"
[[ "$(field "$MKEY" installedVersion)" == "$OLD" ]]
[[ "$(request "$MKEY" '{}')" == 202 ]] || { cat /tmp/peer-update.json; exit 1; }
[[ "$(wait_done "$MKEY")" == succeeded ]] || { item "$MKEY"; exit 1; }
item "$MKEY"
[[ "$(image_of "$NAME")" == "$REGISTRY/estrichone-backup-target:$VERSION" ]]
healthy
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/backup/mirrors/${MKEY#mirror-}"
cleanup_peer
log "Fernaktualisierung erfolgreich: Rücksprung bei Fehler, Update mit erhaltenen Daten, Ziel, das zieht"
