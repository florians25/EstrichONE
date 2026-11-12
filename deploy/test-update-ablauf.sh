#!/usr/bin/env bash
# Update-Ablauf Ende zu Ende (deploy/README.md, „Update-Ablauf“) gegen die laufende Installation in /opt/estrichone
# (CI: infra.yml, Job installer) – mit verkürzten Phasen (Ankündigung 15 s, Update ≥ 15 s, Abschluss 15 s):
#   1. Angemeldete Sitzung, dann update.sh im Hintergrund.
#   2. Ankündigung: /system/notices meldet Phase und Zielversion, Arbeiten geht noch, neue Anmeldung ist gesperrt,
#      Kopfzeile X-EstrichONE-Update auf jeder Antwort, /update-status.json kommt von Caddy.
#   3. Update/Abschluss: alles gesperrt (503 MAINTENANCE, reason update), /update-status.json bleibt erreichbar,
#      auch solange die API getauscht wird.
#   4. Freigabe: Stand weg, alte Sitzung ungültig (401), neue Anmeldung klappt, Gesamtdauer ≥ Summe der Phasen.
#   5. Standarddauer: ohne Angaben dauert der Ablauf mindestens 2,5 Minuten (Trockenlauf von update-phases.sh).
# Benutzer admin wird bei Bedarf eingerichtet (gleiches Passwort wie deploy/backup/test-installer-restore.sh).
# shellcheck disable=SC1111,SC1112
set -euo pipefail

VERSION="${VERSION:?VERSION fehlt}"
DIR=/opt/estrichone
URL=http://localhost:8181
PASSWORD="Estrich trocknet langsam 7"
PHASE=15
[[ $EUID -eq 0 ]] || { echo "Bitte mit Root-Rechten ausführen: sudo -E $0" >&2; exit 2; }

log() { printf '\n== %s\n' "$*"; }
json() { python3 -c "import sys,json; d=json.load(sys.stdin); print(eval('d'+sys.argv[1]))" "$1"; }
api() { curl -sS --noproxy localhost "$@"; }
compose() { (cd "$DIR" && docker compose -p estrichone "$@"); }
status() { api -o /dev/null -w '%{http_code}' "$@"; }
login() {
  api -H 'Content-Type: application/json' "$URL/api/v1/auth/login" \
    -d "{\"login\":\"admin\",\"password\":\"$PASSWORD\",\"client\":\"web\",\"clientName\":\"test\"}"
}
phase() { api "$URL/api/v1/system/notices" | json ".get('update') and d['update']['phase'] or ''" 2>/dev/null || true; }
on_exit() {
  local code=$?
  [[ $code -eq 0 ]] || { echo "Fehlgeschlagen ($code)"; cat "$LOG" 2>/dev/null || true; compose ps; compose logs --tail 60 api caddy; }
}
LOG="$(mktemp)"
trap on_exit EXIT

log "Anmeldung vor dem Update"
if api "$URL/api/v1/server-info" | grep -q '"setupRequired":true'; then
  CODE="$(compose logs api | grep -o 'Einrichtungscode: [A-Z0-9-]*' | tail -n 1 | awk '{print $2}')"
  api --fail-with-body -H 'Content-Type: application/json' -o /dev/null "$URL/api/v1/setup" \
    -d "{\"setupCode\":\"$CODE\",\"companyName\":\"Worst Case GmbH\",\"administrator\":{\"displayName\":\"Admin\",\"userName\":\"admin\",\"email\":null,\"password\":\"$PASSWORD\"}}"
fi
TOKEN="$(login | json "['accessToken']")"
AUTH=(-H "Authorization: Bearer $TOKEN")
test "$(status "${AUTH[@]}" "$URL/api/v1/auth/me")" = 200

log "Wartungsstand ohne API: Caddy liefert /update-status.json, API-Aufrufe Problem-JSON"
STATE="$DIR/data/backup-status/update-maintenance.json"
printf '{"id":"probe","phase":"update","targetVersion":"%s","startedAt":"%s"}\n' "$VERSION" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$STATE"
chmod 644 "$STATE"
compose stop api
api "$URL/update-status.json" | grep -q '"phase":"update"'
test "$(status "$URL/api/v1/server-info")" = 502
api "$URL/api/v1/server-info" | grep -q SERVER_UNAVAILABLE
rm -f "$STATE"
compose start api
for _ in $(seq 1 90); do [[ "$(status "$URL/health/ready" || true)" == 200 ]] && break; sleep 2; done
test "$(status "$URL/health/ready")" = 200
TOKEN="$(login | json "['accessToken']")"
AUTH=(-H "Authorization: Bearer $TOKEN")

log "update.sh mit verkürzten Phasen im Hintergrund"
START="$(date +%s)"
ESTRICHONE_UPDATE_ANNOUNCE_SECONDS=$PHASE ESTRICHONE_UPDATE_MIN_SECONDS=$PHASE ESTRICHONE_UPDATE_FINISH_SECONDS=$PHASE \
  "$DIR/update.sh" --ohne-sicherung "$VERSION" > "$LOG" 2>&1 &
UPDATER=$!

log "Phase 1: Ankündigung"
for _ in $(seq 1 60); do [[ "$(phase)" == announce ]] && break; sleep 1; done
test "$(phase)" = announce
api "$URL/api/v1/system/notices" | tee notices.json | grep -q "\"targetVersion\":\"$VERSION\""
api -D headers.txt -o /dev/null "${AUTH[@]}" "$URL/api/v1/auth/me"
grep -qi "^x-estrichone-update: announce" headers.txt
test "$(status "${AUTH[@]}" "$URL/api/v1/bauakten")" = 200
test "$(login | json "['code']")" = MAINTENANCE
api "$URL/update-status.json" | grep -q '"phase":"announce"'

log "Phase 2: Update – alles gesperrt, Wartungsstand auch ohne API"
for _ in $(seq 1 60); do [[ "$(api "$URL/update-status.json" 2>/dev/null | json "['phase']" 2>/dev/null)" == update ]] && break; sleep 1; done
api "$URL/update-status.json" | grep -q '"phase":"update"'
sleep 4
code="$(status "${AUTH[@]}" "$URL/api/v1/bauakten" || true)"
[[ "$code" == 503 || "$code" == 502 || "$code" == 000 ]] || { echo "Erwartet 503/502 während des Updates, erhalten $code"; exit 1; }
seen_update_file=0
while kill -0 "$UPDATER" 2>/dev/null; do
  p="$(api "$URL/update-status.json" 2>/dev/null | json "['phase']" 2>/dev/null || true)"
  [[ "$p" == update || "$p" == finish ]] && seen_update_file=1
  if [[ "$p" == finish ]]; then
    test "$(status "${AUTH[@]}" "$URL/api/v1/bauakten")" = 503
    api "$URL/api/v1/system/notices" | grep -q '"phase":"finish"'
    break
  fi
  sleep 1
done
test "$seen_update_file" = 1

log "Phase 3: Abschluss und Freigabe"
wait "$UPDATER"
cat "$LOG"
grep -q "Phase 1/3" "$LOG" && grep -q "Phase 2/3" "$LOG" && grep -q "Phase 3/3" "$LOG"
grep -q "Update erfolgreich" "$LOG"
took=$(( $(date +%s) - START ))
echo "Dauer: $took s"
[[ $took -ge $((3 * PHASE)) ]]
test ! -e "$DIR/data/backup-status/update-maintenance.json"
test "$(status "$URL/update-status.json")" = 404
api "$URL/api/v1/system/notices" | json ".get('update')" | grep -qx None
# Alte Sitzung ist beendet, neue Anmeldung klappt.
test "$(status "${AUTH[@]}" "$URL/api/v1/auth/me")" = 401
login | json "['accessToken']" >/dev/null

log "Standarddauer: mindestens 2,5 Minuten (Abschluss 30 s)"
(
  # shellcheck disable=SC1091
  . "$DIR/update-phases.sh"
  unset ESTRICHONE_UPDATE_ANNOUNCE_SECONDS ESTRICHONE_UPDATE_MIN_SECONDS ESTRICHONE_UPDATE_FINISH_SECONDS
  phase_init
  test $((PHASE_ANNOUNCE + PHASE_MIN + PHASE_FINISH)) -ge 150 && test "$PHASE_FINISH" -eq 30
)
echo "Update-Ablauf: alle Prüfungen bestanden."
