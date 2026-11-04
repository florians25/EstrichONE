#!/usr/bin/env bash
# Kombiserver Ende zu Ende (docs/backup.md, „Kombiserver“), gegen den laufenden Stack aus test-installer-restore.sh
# (Benutzer admin):
#   1. Vorhandenes Backup-Ziel (einzeln eingerichtet) mit dem Installer-Modus backup-archiv um das Archiv erweitern:
#      gemeinsames Verzeichnis mit EINER .env (600), getrennte Container, Volumes und Updater.
#   2. EIN Kopplungscode koppelt Backup-Ziel und Archiv (wie „Kombiserver koppeln“ in der Weboberfläche).
#   3. Sicherung landet auf dem Backup-Ziel, „Abschließen & Archivieren“ archiviert die Dokumente der Bauakte.
#   4. Erneut ausführen = idempotent (Kopplungen, Daten, Ports bleiben), beide Dienste einzeln fernaktualisierbar.
#   5. Topologie A: Kombiserver hinter NAT – Backup-Ziel zieht, derselbe Code aus dem Backup Center koppelt das Archiv.
# Erwartet lokal gebaute Images ghcr.io/florians25/estrichone-{installer,backup-target,archive}:$VERSION.
# shellcheck disable=SC1111,SC1112
set -euo pipefail

VERSION="${VERSION:?VERSION fehlt}"
INSTALLER="ghcr.io/florians25/estrichone-installer:$VERSION"
DIR=/opt/estrichone
KOMBI=/opt/estrichone-kombi
URL=http://localhost:8181
NET=estrichone_frontend
PASSWORD="Estrich trocknet langsam 7"
B=estrichone-backup-target
A=estrichone-archive
[[ $EUID -eq 0 ]] || { echo "Bitte mit Root-Rechten ausführen: sudo -E $0" >&2; exit 2; }

log() { printf '\n== %s\n' "$*"; }
json() { python3 -c "import sys,json; d=json.load(sys.stdin); print(eval('d'+sys.argv[1]))" "$1"; }
api() { curl -sS --fail-with-body --noproxy localhost "$@"; }
compose() { (cd "$DIR" && docker compose -p estrichone "$@"); }
cleanup() {
  for c in "$B" "$A"; do docker rm -f "$c" "$c-updater" "$c-updatejob" "$c-vorher" >/dev/null 2>&1 || true; done
  docker volume rm "$B" "$B-update" "$A" "$A-update" >/dev/null 2>&1 || true
  rm -rf "$KOMBI"
}
on_exit() {
  local status=$?
  if [[ $status -ne 0 ]]; then
    echo "Fehlgeschlagen ($status)"
    docker ps -a --format '{{.Names}} {{.Image}} {{.Status}}' | grep -E "$B|$A" || true
    for c in "$B" "$A" "$B-updater" "$A-updater"; do echo "--- $c"; docker logs --tail 30 "$c" 2>&1 || true; done
    compose logs --tail 40 api
  fi
}
trap on_exit EXIT
kombi() { docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$KOMBI:$KOMBI" -e ESTRICHONE_NETWORK="$NET" "$@"; }
not() { if "$@"; then echo "Unerwartet erfolgreich: $*" >&2; exit 1; fi; }
ip_of() { docker inspect -f "{{(index .NetworkSettings.Networks \"$NET\").IPAddress}}" "$1"; }
healthy() { for _ in $(seq 1 40); do docker exec "$1" estrichone-archive healthcheck >/dev/null 2>&1 && return 0; sleep 2; done; return 1; }

AUTH=(-H "Authorization: Bearer $(api -H 'Content-Type: application/json' "$URL/api/v1/auth/login" \
  -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"kombi\"}" | json "['accessToken']")")

log "Ausgangslage: einzeln eingerichtetes Backup-Ziel (noch nicht gekoppelt)"
cleanup
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -e ESTRICHONE_NETWORK="$NET" "$INSTALLER" backup-ziel >/dev/null
B_ID_BEFORE="$(docker container inspect -f '{{.Id}}' "$B")"

log "Ohne eingebundenes Verzeichnis: klare Meldung"
not docker run --rm -v /var/run/docker.sock:/var/run/docker.sock "$INSTALLER" backup-archiv 2>/tmp/kombi-err.log
grep -q "/opt/estrichone-kombi" /tmp/kombi-err.log
mkdir -p "$KOMBI"
not kombi -e ESTRICHONE_BACKUP_DATA_DIR=/srv/kombi -e ESTRICHONE_ARCHIVE_DATA_DIR=/srv/kombi/archiv "$INSTALLER" backup-archiv 2>/tmp/kombi-err.log
grep -q "getrennte Ordner" /tmp/kombi-err.log
not kombi -e ESTRICHONE_BACKUP_PORT=8443 "$INSTALLER" backup-archiv 2>/tmp/kombi-err.log
grep -q "verschiedene Ports" /tmp/kombi-err.log
rm -f "$KOMBI/.env"

log "Kombiserver: vorhandenes Backup-Ziel um das Archiv erweitern (Installer-Modus backup-archiv)"
kombi -e ESTRICHONE_NAME="Kombi Keller" -e ESTRICHONE_MIN_RETENTION_YEARS=10 "$INSTALLER" backup-archiv | tee /tmp/kombi.log
CODE="$(sed -n 's/.*Kopplungscode: *\([A-Z0-9-]*\).*/\1/p' /tmp/kombi.log)"
[[ "$CODE" =~ ^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$ ]] || { echo "Kein Kombi-Code: $CODE"; exit 1; }
grep -q "KEIN zweiter Standort" /tmp/kombi.log
grep -q "Fernaktualisierung: Backup-Ziel an, Archiv an" /tmp/kombi.log
[[ "$(stat -c %a "$KOMBI/.env")" == 600 ]]
grep -q '^ESTRICHONE_BACKUP_PORT="8444"$' "$KOMBI/.env" && grep -q '^ESTRICHONE_ARCHIVE_PORT="8443"$' "$KOMBI/.env"
grep -q "^ESTRICHONE_KOMBI_CODE=\"$CODE\"$" "$KOMBI/.env"
[[ "$(docker container inspect -f '{{.Config.Image}}' "$B")" == "ghcr.io/florians25/estrichone-backup-target:$VERSION" ]]
[[ "$(docker container inspect -f '{{.Config.Image}}' "$A")" == "ghcr.io/florians25/estrichone-archive:$VERSION" ]]
[[ "$(docker container inspect -f '{{.Id}}' "$B")" != "$B_ID_BEFORE" ]]   # neu gestartet, gleiche Daten
# Getrennte Daten: je Dienst ein eigenes Volume, keine gemeinsamen Mounts außer dem Docker-Socket der Updater.
[[ "$(docker container inspect -f '{{range .Mounts}}{{if eq .Destination "/data"}}{{.Name}}{{end}}{{end}}' "$B")" == "$B" ]]
[[ "$(docker container inspect -f '{{range .Mounts}}{{if eq .Destination "/data"}}{{.Name}}{{end}}{{end}}' "$A")" == "$A" ]]
[[ "$(docker port "$B" 8443/tcp | head -n 1)" == *:8444 && "$(docker port "$A" 8443/tcp | head -n 1)" == *:8443 ]]
docker container inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$A" | grep -q '^ESTRICHONE_MIN_RETENTION_YEARS=10$'
docker container inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$B" | grep -q '^ESTRICHONE_NAME=Kombi Keller – Backup-Ziel$'
for c in "$B-updater" "$A-updater"; do [[ "$(docker container inspect -f '{{.State.Running}}' "$c")" == true ]]; done
healthy "$B" && healthy "$A"

log "EIN Code koppelt beide Dienste (wie „Kombiserver koppeln“ im Backup Center)"
TARGET="$(api "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/backup/targets" -d "{\"address\":\"$(ip_of "$B"):8443\",\"code\":\"$CODE\"}")"
TARGET_ID="$(json "['id']" <<<"$TARGET")"
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/archive/pair" -d "{\"address\":\"$(ip_of "$A"):8443\",\"code\":\"$CODE\"}"
[[ "$(api "${AUTH[@]}" "$URL/api/v1/archive/status" | json "['connected']")" == True ]]
# Code ist bei beiden verbraucht
[[ "$(curl -sS --noproxy localhost -o /dev/null -w '%{http_code}' "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/archive/pair" \
  -d "{\"address\":\"$(ip_of "$A"):8443\",\"code\":\"$CODE\"}")" == 422 ]]

log "Sicherung auf das Backup-Ziel des Kombiservers"
compose exec -T backup estrichone-backup run >/dev/null
SNAPSHOTS="$(api "${AUTH[@]}" "$URL/api/v1/backup/targets" | json "[0]['remote']['snapshots']")"
[[ "$SNAPSHOTS" -ge 1 ]] || { echo "Keine Sicherung auf dem Kombiserver"; exit 1; }

log "Abschließen & Archivieren: Bauakte abschließen, Dokumente sofort archivieren"
BAUAKTE=01a15700-0000-7000-8000-00000000c0b1
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/bauakten" -d "{\"id\":\"$BAUAKTE\",\"title\":\"Kombi Abschluss\"}"
DOC="$(mktemp --suffix=.pdf)"
printf '%%PDF-1.4\n%% Aufmass Kombi %s\n1 0 obj<<>>endobj\ntrailer<<>>\n%%%%EOF\n' "$(date +%s%N)" > "$DOC"
FILE=01a15700-0000-7000-8000-00000000f0b1
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/files" \
  -d "{\"id\":\"$FILE\",\"bauakteId\":\"$BAUAKTE\",\"kind\":\"document\",\"originalName\":\"Aufmass.pdf\",\"contentType\":\"application/pdf\",\"sizeBytes\":$(stat -c %s "$DOC"),\"sha256\":\"$(sha256sum "$DOC" | cut -d' ' -f1)\",\"capturedAt\":\"2026-11-03T09:15:00+01:00\"}"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/octet-stream' --data-binary @"$DOC" -o /dev/null "$URL/api/v1/files/$FILE/content"
VERSION_TAG="$(api "${AUTH[@]}" "$URL/api/v1/bauakten/$BAUAKTE" | json "['version']")"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/json' -H "If-Match: \"$VERSION_TAG\"" -o /dev/null "$URL/api/v1/bauakten/$BAUAKTE" \
  -d '{"title":"Kombi Abschluss","status":"completed"}'
[[ "$(api "${AUTH[@]}" "$URL/api/v1/system/health-banner" | json "['archive']['configured']")" == True ]]
[[ "$(api "${AUTH[@]}" -X POST "$URL/api/v1/archive/bauakten/$BAUAKTE?reason=completion" | json "['queued']")" -ge 1 ]]
STATUS=""
for _ in $(seq 1 40); do
  STATUS="$(api "${AUTH[@]}" "$URL/api/v1/archive/bauakten/$BAUAKTE" | json "[0]['status']" 2>/dev/null || true)"
  [[ "$STATUS" == archived ]] && break
  sleep 3
done
[[ "$STATUS" == archived ]] || { api "${AUTH[@]}" "$URL/api/v1/archive/bauakten/$BAUAKTE"; exit 1; }
[[ "$(api "${AUTH[@]}" "$URL/api/v1/bauakten/$BAUAKTE" | json "['status']")" == completed ]]
docker exec "$A" estrichone-archive verify | grep -q '"ok": true'
api "${AUTH[@]}" "$URL/api/v1/audit?action=archive.bauakte_queued" | grep -q completion

log "Erneut ausführen: idempotent, Kopplungen und Daten bleiben, kein neuer Code"
kombi "$INSTALLER" backup-archiv | tee /tmp/kombi2.log
grep -q "Beide Dienste sind bereits gekoppelt" /tmp/kombi2.log
not grep -q "Kopplungscode:" /tmp/kombi2.log
not grep -q '^ESTRICHONE_KOMBI_CODE=' "$KOMBI/.env"
docker container inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$A" | grep -q '^ESTRICHONE_MIN_RETENTION_YEARS=10$'
healthy "$B" && healthy "$A"
compose exec -T backup estrichone-backup run >/dev/null
[[ "$(api "${AUTH[@]}" "$URL/api/v1/backup/targets" | json "[0]['remote']['snapshots']")" -gt "$SNAPSHOTS" ]]
[[ "$(api "${AUTH[@]}" "$URL/api/v1/archive/status" | json "['server']['name']")" == "Kombi Keller – Archiv" ]]

log "Fernaktualisierung: beide Dienste einzeln (eigener Updater je Dienst)"
for _ in $(seq 1 20); do
  PEERS="$(api "${AUTH[@]}" "$URL/api/v1/backup/peer-updates")"
  [[ "$(python3 -c "import sys,json; print(sum(1 for x in json.load(sys.stdin)['items'] if x['updaterAvailable']))" <<<"$PEERS")" -ge 2 ]] && break
  sleep 3
done
python3 -c "import sys,json; d={x['key']:x for x in json.load(sys.stdin)['items']}; assert d[sys.argv[1]]['updaterAvailable'] and d['archive']['updaterAvailable'], d" \
  "target-${TARGET_ID//-/}" <<<"$PEERS"

log "Aufräumen (entkoppeln)"
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/backup/targets/$TARGET_ID"
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/archive/connection"
cleanup

log "Topologie A: Kombiserver hinter NAT – Backup-Ziel zieht, derselbe Code koppelt das Archiv"
MIRROR="$(api "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/backup/mirrors" -d '{"name":"Kombi zuhause","combined":true}')"
MCODE="$(json "['code']" <<<"$MIRROR")"
MID="$(json "['mirror']['id']" <<<"$MIRROR")"
json "['command']" <<<"$MIRROR" | grep -q " backup-archiv$"
mkdir -p "$KOMBI"
kombi -e ESTRICHONE_PULL_FROM=http://api:8080 -e ESTRICHONE_PULL_CODE="$MCODE" -e ESTRICHONE_PULL_INTERVAL_MINUTES=5 \
  "$INSTALLER" backup-archiv | tee /tmp/kombi3.log
grep -q "zieht von http://api:8080" /tmp/kombi3.log
grep -q "Kopplungscode: *$MCODE" /tmp/kombi3.log
not grep -q "ESTRICHONE_PULL_CODE" "$KOMBI/.env"   # Einmal-Code wird nicht gespeichert
grep -q '^ESTRICHONE_PULL_FROM="http://api:8080"$' "$KOMBI/.env"
for _ in $(seq 1 40); do
  [[ "$(api "${AUTH[@]}" "$URL/api/v1/backup/mirrors" | python3 -c "import sys,json; print(next(m for m in json.load(sys.stdin)['mirrors'] if m['id']==sys.argv[1])['paired'])" "$MID")" == True ]] && break
  sleep 3
done
[[ "$(api "${AUTH[@]}" "$URL/api/v1/backup/mirrors" | python3 -c "import sys,json; print(next(m for m in json.load(sys.stdin)['mirrors'] if m['id']==sys.argv[1])['paired'])" "$MID")" == True ]]
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/archive/pair" -d "{\"address\":\"$(ip_of "$A"):8443\",\"code\":\"$MCODE\"}"
[[ "$(api "${AUTH[@]}" "$URL/api/v1/archive/status" | json "['connected']")" == True ]]
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/archive/connection"
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/backup/mirrors/$MID"
cleanup
log "Kombiserver erfolgreich: Erweiterung, ein Code, Sicherung, Abschließen & Archivieren, idempotent, Topologie A"
