#!/usr/bin/env bash
# Speicher-Auslagerung Ende zu Ende (docs/speicher.md, „Auslagerung“), gegen den laufenden Stack aus test-installer-restore.sh
# (Benutzer admin):
#   1. Bestehender Kombiserver (Backup-Ziel + Archiv mit eigenen Ordnern, gekoppelt) wird mit EINEM erneuten Installer-Lauf
#      um die Auslagerung erweitert (-e ESTRICHONE_OFFLOAD_DATA_DIR=…): eigener Container, Ordner, Port, Updater; nur der neue
#      Dienst bekommt einen Code, Backup-Ziel und Archiv bleiben gekoppelt.
#   2. Koppeln, Dokumente auslagern: lokal erst gelöscht, nachdem der Backup-Dienst die Sicherungskopie (eigener Snapshot)
#      bestätigt hat; auf dem Storage nur Chiffretext; Abruf transparent und bitgenau.
#   3. Storage aus: klare Meldung (503 OFFLOAD_UNAVAILABLE), gelbes Banner für die Verwaltung; wieder an: abrufbar.
#   4. Storage-Daten verloren: Abgleich meldet „fehlt“, Rückholen aus der Sicherung (Backup-Dienst) stellt die Originale her.
#   5. Erneut ausführen = idempotent; Fernaktualisierung des Storage; alles zurückholen, entkoppeln.
# Erwartet lokal gebaute Images ghcr.io/florians25/estrichone-{installer,backup-target,archive}:$VERSION.
# shellcheck disable=SC1111,SC1112,SC2016
set -euo pipefail

VERSION="${VERSION:?VERSION fehlt}"
INSTALLER="ghcr.io/florians25/estrichone-installer:$VERSION"
DIR=/opt/estrichone
KOMBI=/opt/estrichone-kombi
DATA=/srv/eo-kombi
URL=http://localhost:8181
NET=estrichone_frontend
PASSWORD="Estrich trocknet langsam 7"
B=estrichone-backup-target
A=estrichone-archive
O=estrichone-offload
[[ $EUID -eq 0 ]] || { echo "Bitte mit Root-Rechten ausführen: sudo -E $0" >&2; exit 2; }

log() { printf '\n== %s\n' "$*"; }
json() { python3 -c "import sys,json; d=json.load(sys.stdin); print(eval('d'+sys.argv[1]))" "$1"; }
api() { curl -sS --fail-with-body --noproxy localhost "$@"; }
compose() { (cd "$DIR" && docker compose -p estrichone "$@"); }
cleanup() {
  for c in "$B" "$A" "$O"; do docker rm -f "$c" "$c-updater" "$c-updatejob" "$c-vorher" >/dev/null 2>&1 || true; done
  docker volume rm "$B" "$B-update" "$A" "$A-update" "$O" "$O-update" >/dev/null 2>&1 || true
  rm -rf "$KOMBI" "$DATA"
}
on_exit() {
  local status=$?
  if [[ $status -ne 0 ]]; then
    echo "Fehlgeschlagen ($status)"
    docker ps -a --format '{{.Names}} {{.Image}} {{.Status}}' | grep -E "$B|$A|$O" || true
    for c in "$O" "$O-updater"; do echo "--- $c"; docker logs --tail 30 "$c" 2>&1 || true; done
    compose logs --tail 40 api backup
  fi
}
trap on_exit EXIT
kombi() { docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$KOMBI:$KOMBI" -e ESTRICHONE_NETWORK="$NET" "$@"; }
not() { if "$@"; then echo "Unerwartet erfolgreich: $*" >&2; exit 1; fi; }
ip_of() { docker inspect -f "{{(index .NetworkSettings.Networks \"$NET\").IPAddress}}" "$1"; }
healthy() { for _ in $(seq 1 40); do docker exec "$1" estrichone-archive healthcheck >/dev/null 2>&1 && return 0; sleep 2; done; return 1; }
overview() { api "${AUTH[@]}" "$URL/api/v1/files/offload"; }
wait_for() { # wait_for <sekunden> <python-ausdruck über d = Übersicht>
  local deadline=$(( $(date +%s) + $1 ))
  while (( $(date +%s) < deadline )); do
    overview | python3 -c "import sys,json; d=json.load(sys.stdin); sys.exit(0 if ($2) else 1)" && return 0
    sleep 3
  done
  overview; return 1
}

AUTH=(-H "Authorization: Bearer $(api -H 'Content-Type: application/json' "$URL/api/v1/auth/login" \
  -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"auslagerung\"}" | json "['accessToken']")")

log "Ausgangslage: Kombiserver wie beim Nutzer (Backup-Ziel + Archiv, eigene Ordner), beide gekoppelt"
cleanup
mkdir -p "$KOMBI"
kombi -e ESTRICHONE_NAME="Kombi Storage" -e ESTRICHONE_BACKUP_DATA_DIR="$DATA/backup" -e ESTRICHONE_ARCHIVE_DATA_DIR="$DATA/archiv" \
  "$INSTALLER" backup-archiv | tee /tmp/offload-kombi1.log
CODE="$(sed -n 's/.*Kopplungscode: *\([A-Z0-9-]*\).*/\1/p' /tmp/offload-kombi1.log)"
grep -q "ESTRICHONE_OFFLOAD_DATA_DIR" /tmp/offload-kombi1.log   # Hinweis auf die optionale Auslagerung
not docker container inspect "$O" >/dev/null 2>&1
TARGET_ID="$(api "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/backup/targets" -d "{\"address\":\"$(ip_of "$B"):8443\",\"code\":\"$CODE\"}" | json "['id']")"
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/archive/pair" -d "{\"address\":\"$(ip_of "$A"):8443\",\"code\":\"$CODE\"}"
B_ID="$(docker container inspect -f '{{.Id}}' "$B")"

log "Fehlbedienung: Auslagerungsordner im Backup-Ordner wird abgelehnt"
not kombi -e ESTRICHONE_OFFLOAD_DATA_DIR="$DATA/backup/auslagerung" "$INSTALLER" backup-archiv 2>/tmp/offload-err.log
grep -q "getrennte Ordner" /tmp/offload-err.log
not kombi -e ESTRICHONE_OFFLOAD_DATA_DIR="$DATA/auslagerung" -e ESTRICHONE_OFFLOAD_PORT=8444 "$INSTALLER" backup-archiv 2>/tmp/offload-err.log
grep -q "eigenen Port" /tmp/offload-err.log
not docker container inspect "$O" >/dev/null 2>&1

log "Erweitern: derselbe Befehl mit -e ESTRICHONE_OFFLOAD_DATA_DIR – nur der neue Dienst bekommt einen Code"
kombi -e ESTRICHONE_OFFLOAD_DATA_DIR="$DATA/auslagerung" "$INSTALLER" backup-archiv | tee /tmp/offload-kombi2.log
grep -q "3/3 Speicher-Auslagerung" /tmp/offload-kombi2.log
OCODE="$(sed -n 's/.*Kopplungscode: *\([A-Z0-9-]*\).*/\1/p' /tmp/offload-kombi2.log)"
[[ "$OCODE" =~ ^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$ ]] || { echo "Kein Code für die Auslagerung: $OCODE"; exit 1; }
grep -q "System → Speicher-Auslagerung → „Storage koppeln“" /tmp/offload-kombi2.log
grep -q "Fernaktualisierung: Backup-Ziel an, Archiv an, Storage an" /tmp/offload-kombi2.log
grep -q '^ESTRICHONE_OFFLOAD="an"$' "$KOMBI/.env" && grep -q "^ESTRICHONE_OFFLOAD_DATA_DIR=\"$DATA/auslagerung\"$" "$KOMBI/.env"
grep -q "^ESTRICHONE_BACKUP_DATA_DIR=\"$DATA/backup\"$" "$KOMBI/.env"
[[ "$(stat -c %a "$KOMBI/.env")" == 600 ]]
[[ "$(docker container inspect -f '{{.Config.Image}}' "$O")" == "ghcr.io/florians25/estrichone-archive:$VERSION" ]]
docker container inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$O" | grep -q '^ESTRICHONE_ROLE=offload$'
[[ "$(docker container inspect -f '{{range .Mounts}}{{if eq .Destination "/data"}}{{.Source}}{{end}}{{end}}' "$O")" == "$DATA/auslagerung" ]]
[[ "$(docker port "$O" 8443/tcp | head -n 1)" == *:8445 ]]
[[ "$(docker container inspect -f '{{.State.Running}}' "$O-updater")" == true ]]
healthy "$O"
# Backup-Ziel und Archiv: weiterhin gekoppelt, Daten im bisherigen Ordner
[[ "$(api "${AUTH[@]}" "$URL/api/v1/archive/status" | json "['connected']")" == True ]]
[[ "$(docker container inspect -f '{{range .Mounts}}{{if eq .Destination "/data"}}{{.Source}}{{end}}{{end}}' "$B")" == "$DATA/backup" ]]
[[ "$(docker container inspect -f '{{.Id}}' "$B")" != "$B_ID" ]]   # aktualisiert, gleiche Daten
compose exec -T backup estrichone-backup run >/dev/null
[[ "$(api "${AUTH[@]}" "$URL/api/v1/backup/targets" | json "[0]['remote']['snapshots']")" -ge 1 ]]

log "Storage koppeln (gleicher Code-Mechanismus, gepinntes Zertifikat)"
api "${AUTH[@]}" -H 'Content-Type: application/json' "$URL/api/v1/files/offload/pair" -d "{\"address\":\"$(ip_of "$O"):8443\",\"code\":\"$OCODE\"}" \
  | json "['serverName']" | grep -q "Kombi Storage – Storage"
api "${AUTH[@]}" -X PUT -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/files/offload/settings" -d '{"autoWhenLow":false,"autoByRules":false,
  "thresholdPercent":15,"thresholdFreeGb":null,"minAgeMonths":0,"onlyCompletedBauakten":false,"includePhotos":true,"includeVideos":true,
  "includeDocuments":true,"requireBackup":true,"cacheLimitMb":500}'

log "Zwei Dokumente anlegen und auslagern"
BAUAKTE=01a15700-0000-7000-8000-00000000d0f1
api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/bauakten" -d "{\"id\":\"$BAUAKTE\",\"title\":\"Auslagerung E2E\"}"
declare -A SHA
FILES=(01a15700-0000-7000-8000-00000000d1f1 01a15700-0000-7000-8000-00000000d1f2)
for FILE in "${FILES[@]}"; do
  DOC="$(mktemp --suffix=.pdf)"
  printf '%%PDF-1.4\n%% GEHEIMER-INHALT-%s %s\n1 0 obj<<>>endobj\ntrailer<<>>\n%%%%EOF\n' "$FILE" "$(date +%s%N)" > "$DOC"
  api "${AUTH[@]}" -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/files" \
    -d "{\"id\":\"$FILE\",\"bauakteId\":\"$BAUAKTE\",\"kind\":\"document\",\"originalName\":\"Plan.pdf\",\"contentType\":\"application/pdf\",\"sizeBytes\":$(stat -c %s "$DOC"),\"sha256\":\"$(sha256sum "$DOC" | cut -d' ' -f1)\",\"category\":\"plan\"}"
  api "${AUTH[@]}" -X PUT -H 'Content-Type: application/octet-stream' --data-binary @"$DOC" -o /dev/null "$URL/api/v1/files/$FILE/content"
  SHA[$FILE]="$(api "${AUTH[@]}" "$URL/api/v1/files/$FILE" | json "['sha256']")"
  rm -f "$DOC"
done
local_original() { find "$DIR/data/files/originals" -name "${1//-/}" | head -n 1; }
[[ -n "$(local_original "${FILES[0]}")" ]]
[[ "$(api "${AUTH[@]}" -X POST -H 'Content-Type: application/json' -o /dev/null -w '%{http_code}' "$URL/api/v1/files/offload/run" -d "{\"bauakteId\":\"$BAUAKTE\"}")" == 202 ]]
# Erst übertragen und geprüft, dann Sicherungskopie (Backup-Dienst, ≤ 30 s), dann lokal gelöscht.
wait_for 180 "d['counts']['offloadedBlobs'] >= 2 and d['counts']['pendingBlobs'] == 0"
for FILE in "${FILES[@]}"; do [[ -z "$(local_original "$FILE")" ]] || { echo "Original noch lokal: $FILE"; exit 1; }; done
overview | json "['lastCopy']['status']" | grep -qx succeeded
compose exec -T backup sh -c 'restic -r "$BACKUP_REPOSITORY" snapshots --tag estrichone-auslagerung --json' | grep -q '"paths"'
# Eigener Snapshot (nicht Tag „estrichone“): „latest“ und Aufbewahrung der normalen Sicherungen lassen ihn unberührt.
compose exec -T backup sh -c 'restic -r "$BACKUP_REPOSITORY" snapshots --tag estrichone-auslagerung --json' | not grep -q '"estrichone"'
# Auf dem Storage nur Chiffretext
not grep -rq "GEHEIMER-INHALT" "$DATA/auslagerung"
[[ "$(find "$DATA/auslagerung/offload" -path '*/objects/*' -type f ! -name '*.json' | wc -l)" -ge 2 ]]

log "Abruf transparent und bitgenau (Original vom Storage)"
for FILE in "${FILES[@]}"; do
  curl -sS --noproxy localhost "${AUTH[@]}" -D /tmp/offload-headers.txt -o /tmp/offload-file.bin "$URL/api/v1/files/$FILE/content"
  grep -qi "^x-estrichone-storage: offload" /tmp/offload-headers.txt
  [[ "$(sha256sum /tmp/offload-file.bin | cut -d' ' -f1)" == "${SHA[$FILE]}" ]]
done
api "${AUTH[@]}" "$URL/api/v1/files/offload/locations?ids=${FILES[0]}" | grep -q "${FILES[0]}"

log "Storage aus: klare Meldung, gelbes Banner für die Verwaltung; wieder an: abrufbar"
docker stop "$O" >/dev/null
rm -rf "$DIR/data/files/.offload-cache"
[[ "$(curl -sS --noproxy localhost "${AUTH[@]}" -o /tmp/offload-problem.json -w '%{http_code}' "$URL/api/v1/files/${FILES[0]}/content")" == 503 ]]
grep -q OFFLOAD_UNAVAILABLE /tmp/offload-problem.json
BANNER=""
for _ in $(seq 1 60); do
  BANNER="$(api "${AUTH[@]}" "$URL/api/v1/system/health-banner")"
  grep -q '"key":"offload"' <<<"$BANNER" && break
  sleep 3
done
grep -q '"key":"offload"' <<<"$BANNER"
docker start "$O" >/dev/null && healthy "$O"
[[ "$(curl -sS --noproxy localhost "${AUTH[@]}" -o /dev/null -w '%{http_code}' "$URL/api/v1/files/${FILES[0]}/content")" == 200 ]]

log "Storage-Daten verloren: Abgleich meldet „fehlt“, Rückholen aus der Sicherung stellt die Originale her"
find "$DATA/auslagerung/offload" -path '*/objects/*' -type f -delete
rm -rf "$DIR/data/files/.offload-cache"
api "${AUTH[@]}" -X POST -o /dev/null "$URL/api/v1/files/offload/reconcile"
wait_for 30 "d['counts']['missingBlobs'] >= 2 and d['status']['level'] == 'critical'"
[[ "$(api "${AUTH[@]}" -X POST -o /dev/null -w '%{http_code}' "$URL/api/v1/files/offload/recover-from-backup")" == 202 ]]
for _ in $(seq 1 60); do
  [[ -n "$(local_original "${FILES[0]}")" && -n "$(local_original "${FILES[1]}")" ]] && break
  sleep 3
done
for FILE in "${FILES[@]}"; do [[ "$(sha256sum "$(local_original "$FILE")" | cut -d' ' -f1)" == "${SHA[$FILE]}" ]]; done
api "${AUTH[@]}" -X POST -o /dev/null "$URL/api/v1/files/offload/reconcile"
wait_for 60 "d['counts']['missingBlobs'] == 0 and d['counts']['offloadedBlobs'] == 0"

log "Erneut ausführen: idempotent, alle Dienste bleiben gekoppelt, kein neuer Code"
kombi "$INSTALLER" backup-archiv | tee /tmp/offload-kombi3.log
grep -q "Alle Dienste sind bereits gekoppelt" /tmp/offload-kombi3.log
not grep -q "Kopplungscode:" /tmp/offload-kombi3.log
healthy "$O"
[[ "$(overview | json "['connection']['reachable']")" == True ]]
[[ "$(api "${AUTH[@]}" "$URL/api/v1/archive/status" | json "['connected']")" == True ]]

log "Fernaktualisierung: Storage mit eigenem Updater"
for _ in $(seq 1 20); do
  api "${AUTH[@]}" "$URL/api/v1/backup/peer-updates" | python3 -c "import sys,json; d={x['key']:x for x in json.load(sys.stdin)['items']}; sys.exit(0 if d.get('offload',{}).get('updaterAvailable') else 1)" && break
  sleep 3
done
api "${AUTH[@]}" "$URL/api/v1/backup/peer-updates" | python3 -c "import sys,json; d={x['key']:x for x in json.load(sys.stdin)['items']}; assert d['offload']['updaterAvailable'] and d['offload']['kind']=='offload', d"

log "Erneut auslagern, dann alles wieder lokal speichern und entkoppeln"
api "${AUTH[@]}" -X POST -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/files/offload/run" -d "{\"bauakteId\":\"$BAUAKTE\"}"
wait_for 180 "d['counts']['offloadedBlobs'] >= 2 and d['counts']['pendingBlobs'] == 0"
[[ "$(curl -sS --noproxy localhost "${AUTH[@]}" -o /dev/null -w '%{http_code}' -X DELETE "$URL/api/v1/files/offload/pairing")" == 409 ]]
api "${AUTH[@]}" -X POST -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/files/offload/recall" -d '{"all":true}'
wait_for 120 "d['counts']['offloadedBlobs'] == 0 and not d['job']['running']"
for FILE in "${FILES[@]}"; do [[ "$(sha256sum "$(local_original "$FILE")" | cut -d' ' -f1)" == "${SHA[$FILE]}" ]]; done
[[ "$(find "$DATA/auslagerung/offload" -path '*/trash/*' -type f ! -name '*.json' | wc -l)" -ge 2 ]]   # Papierkorb auf dem Storage
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/files/offload/pairing"
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/backup/targets/$TARGET_ID"
api "${AUTH[@]}" -X DELETE -o /dev/null "$URL/api/v1/archive/connection"
cleanup
log "Speicher-Auslagerung erfolgreich: Erweiterung per Installer, Auslagern mit Sicherungskopie, Abruf, Ausfall, Verlust + Rückholen aus der Sicherung, idempotent, Rückholen"
