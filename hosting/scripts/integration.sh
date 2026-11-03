#!/usr/bin/env bash
# Echter Integrationstest der Hosting-Konsole (CI: Linux-Runner mit Docker):
# Ein-Befehl-Installation (hosting/install.sh) → Admin aus dem Installer, Passwortwechsel, Einrichtungsassistent →
# Instanz per Assistent mit erster Admin-Person anlegen (Zugangsbrief, Anmeldung in der Suite) → Health über den
# gemeinsamen Proxy → stoppen/starten, sperren/entsperren, sichern → Aktualisierung mit Vorab-Sicherung und Rücknahme →
# Wiederherstellung → Export → löschen mit Endsicherung → Archiv → Reparatur-Lauf → Deinstallation.
#   IMAGE=estrichone/hosting:ci SUITE_VERSION=0.1.0-ci SUITE_VERSION_NEXT=0.1.0-ci.2 hosting/scripts/integration.sh
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112
set -euo pipefail

IMAGE="${IMAGE:-estrichone/hosting:ci}"
SUITE_VERSION="${SUITE_VERSION:-0.1.0-ci}"
SUITE_VERSION_NEXT="${SUITE_VERSION_NEXT:-0.1.0-ci.2}"
HERE="$(cd "$(dirname "$0")/.." && pwd)"
ROOT="${HOSTING_ROOT:-/srv/estrichone-hosting-ci}"
HTTP_PORT="${HTTP_PORT:-8088}"
CONSOLE="http://localhost:${CONSOLE_PORT:-8099}"
SLUG=ci-test
DOMAIN="$SLUG.hosting.test"
JAR="$(mktemp)"
CSRF=""

log() { printf '\n=== %s\n' "$*"; }
fail() { echo "FEHLER: $*" >&2; exit 1; }
api() { # api METHODE PFAD [JSON]
  local args=(-sS -b "$JAR" -c "$JAR" -X "$1" -H 'Accept: application/json' -w '\n%{http_code}')
  [[ -n "$CSRF" ]] && args+=(-H "X-CSRF-Token: $CSRF")
  [[ $# -ge 3 ]] && args+=(-H 'Content-Type: application/json' -d "$3")
  curl "${args[@]}" "$CONSOLE$2"
}
body() { sed '$d' <<<"$1"; }
status() { tail -n1 <<<"$1"; }
expect_status() { # expect_status ERWARTET ANTWORT TEXT
  [[ "$(status "$2")" == "$1" ]] || fail "$3: HTTP $(status "$2") statt $1 – $(body "$2")"
}
wait_operation() { # wait_operation ID MAX_SEKUNDEN
  local id="$1" deadline=$((SECONDS + $2)) result state
  while (( SECONDS < deadline )); do
    result="$(api GET "/api/operations/$id")"
    state="$(body "$result" | jq -r .status)"
    if [[ "$state" == "Succeeded" ]]; then return 0; fi
    if [[ "$state" == "Failed" ]]; then body "$result" | jq -r .log; fail "Vorgang fehlgeschlagen: $(body "$result" | jq -r .error)"; fi
    sleep 5
  done
  api GET "/api/operations/$id" | sed '$d' | jq -r .log
  fail "Vorgang $id nicht rechtzeitig abgeschlossen"
}
action() { # action AKTION [JSON] → wartet auf den Vorgang
  local result payload="${2:-}"
  [[ -n "$payload" ]] || payload='{}'
  result="$(api POST "/api/tenants/$TENANT/actions/$1" "$payload")"
  expect_status 202 "$result" "Aktion $1"
  wait_operation "$(body "$result" | jq -r .id)" "${3:-600}"
  echo "Aktion $1: erfolgreich"
}
proxy() { # proxy PFAD [JSON] → HTTP-Status über den gemeinsamen Caddy
  local args=(-s -o /tmp/proxy-body -w '%{http_code}' -H "Host: $DOMAIN")
  [[ $# -ge 2 ]] && args+=(-H 'Content-Type: application/json' -d "$2")
  curl "${args[@]}" "http://localhost:$HTTP_PORT$1"
}
suite_login() { # suite_login BENUTZER PASSWORT → Anmeldung in der Suite über den Proxy
  [[ "$(proxy /api/v1/auth/login "{\"login\":\"$1\",\"password\":\"$2\",\"client\":\"web\",\"clientName\":\"ci\"}")" == "200" ]] \
    || fail "Anmeldung von $1 in der Suite fehlgeschlagen: $(head -c 300 /tmp/proxy-body)"
}
wait_proxy() { # wait_proxy PFAD STATUS
  for _ in $(seq 1 60); do [[ "$(proxy "$1")" == "$2" ]] && return 0; sleep 2; done
  fail "Proxy liefert für $1 nicht HTTP $2 (zuletzt $(proxy "$1"): $(head -c 300 /tmp/proxy-body))"
}

log "Ein-Befehl-Installation (hosting/install.sh, $IMAGE)"
sudo env HOSTING_ROOT="$ROOT" HOSTING_IMAGE="$IMAGE" HOSTING_PULL=never HOSTING_NONINTERACTIVE=1 \
  HOSTING_BASE_DOMAIN=hosting.test HOSTING_CONSOLE_DOMAIN=konsole.hosting.test HOSTING_MODE=direkt HOSTING_TLS=off \
  HOSTING_HTTP_PORT="$HTTP_PORT" HOSTING_HTTPS_PORT=8443 HOSTING_ADMIN_USER=chefin HOSTING_ADMIN_EMAIL=ci@example.org \
  HOSTING_DEFAULT_VERSION="$SUITE_VERSION" bash "$HERE/install.sh" | tee /tmp/install.log
START_PASSWORD="$(sed -n 's/^  Startpasswort: \([A-Za-z0-9-]*\)$/\1/p' /tmp/install.log | tail -n1)"
[[ "$START_PASSWORD" =~ ^[A-Za-z0-9]{4}(-[A-Za-z0-9]{4}){3}$ ]] || fail "Installer hat kein Startpasswort ausgegeben"
grep -q "Adresse:      http://konsole.hosting.test" /tmp/install.log || fail "Abschlussausgabe ohne Adresse"
sudo test -f "$ROOT/.env" || fail ".env fehlt"
[[ "$(sudo stat -c %a "$ROOT/.env")" == "600" ]] || fail ".env ist nicht nur für root lesbar"
sudo test ! -f "$ROOT/console/initial-admin.json" || fail "Startpasswort-Datei wurde nicht gelöscht"
[[ -x /usr/local/bin/estrichone-hosting ]] || fail "Verwaltungsbefehl estrichone-hosting fehlt"
sudo estrichone-hosting status | grep estrichone-hosting-console >/dev/null || fail "status zeigt die Konsole nicht"

log "Anmeldung mit dem Konto aus dem Installer, Passwortwechsel, Einrichtungsassistent"
result="$(api POST /api/auth/login "{\"userName\":\"chefin\",\"password\":\"$START_PASSWORD\"}")"
expect_status 204 "$result" "Anmeldung mit Startpasswort"
CSRF="$(api GET /api/auth/state | sed '$d' | jq -r .csrfToken)"
[[ "$(api GET /api/setup-wizard | sed '$d' | jq -r .mustChangePassword)" == "true" ]] || fail "Passwortwechsel wird nicht verlangt"
expect_status 403 "$(api POST /api/setup-wizard/complete '{}')" "Assistent ohne Passwortwechsel"
expect_status 403 "$(api GET /api/tenants)" "API mit Startpasswort"
expect_status 204 "$(api POST /api/account/password "{\"currentPassword\":\"$START_PASSWORD\",\"newPassword\":\"ci-sehr-geheimes-passwort\"}")" "Passwortwechsel"
CSRF="$(api GET /api/auth/state | sed '$d' | jq -r .csrfToken)"
expect_status 204 "$(api POST /api/setup-wizard/complete "{\"defaultVersion\":\"$SUITE_VERSION\"}")" "Einrichtungsassistent"
[[ "$(api GET /api/setup-wizard | sed '$d' | jq -r .completed)" == "true" ]] || fail "Assistent nicht abgeschlossen"
[[ "$(curl -s -o /dev/null -w '%{http_code}' -H 'Host: konsole.hosting.test' "http://localhost:$HTTP_PORT/health")" == "200" ]] \
  || fail "Konsole über den Proxy nicht erreichbar"

log "Instanz per Assistent anlegen (erste Admin-Person, Suite $SUITE_VERSION)"
PLAN="$(api GET /api/plans | sed '$d' | jq -r '.[0].id')"
result="$(api POST /api/tenants "{\"companyName\":\"CI Estrich GmbH\",\"customerNumber\":\"CI-1\",\"contactEmail\":\"ci@example.org\",\"slug\":\"$SLUG\",\"planId\":\"$PLAN\",\"version\":\"$SUITE_VERSION\",\"firstAdminName\":\"Petra Polier\",\"firstAdminUserName\":\"petra\",\"firstAdminEmail\":\"petra@example.org\"}")"
expect_status 201 "$result" "Mandant anlegen"
TENANT="$(body "$result" | jq -r .id)"
wait_operation "$(body "$result" | jq -r .openOperation.id)" 1200
sudo test -f "$ROOT/instances/$SLUG/.env" || fail "Instanzverzeichnis fehlt"
[[ -z "$(docker ps --filter "label=com.docker.compose.project=estrichone-$SLUG" --format '{{.Ports}}' | grep -E '0\.0\.0\.0|:::' || true)" ]] \
  || fail "Instanz veröffentlicht Ports"
[[ "$(sudo stat -c %u "$ROOT/instances/$SLUG/data/files")" == "1654" ]] || fail "Datenverzeichnis gehört nicht UID 1654"

log "Zugangsbrief und Anmeldung der ersten Admin-Person in der Suite"
expect_status 400 "$(api POST "/api/tenants/$TENANT/access-letter" '{"password":"falsch"}')" "Zugangsbrief mit falschem Passwort"
letter="$(api POST "/api/tenants/$TENANT/access-letter" '{"password":"ci-sehr-geheimes-passwort"}')"
expect_status 200 "$letter" "Zugangsbrief"
SUITE_PASSWORD="$(body "$letter" | jq -r .initialPassword)"
[[ "$(body "$letter" | jq -r .userName)" == "petra" ]] || fail "Zugangsbrief: falscher Benutzer"
wait_proxy /health/ready 200
jq -e '.setupRequired == false' <(proxy /api/v1/server-info >/dev/null; cat /tmp/proxy-body) >/dev/null || fail "Suite nicht eingerichtet: $(cat /tmp/proxy-body)"
suite_login petra "$SUITE_PASSWORD"
detail="$(api POST "/api/tenants/$TENANT/refresh" | sed '$d')"
echo "$detail" | jq '{health, setupRequired, runningVersion, problems, containers: [.containers[] | {service, state, health}]}'
[[ "$(jq -r .health <<<"$detail")" == "Healthy" ]] || fail "Instanz nicht gesund"
logs="$(api GET "/api/tenants/$TENANT/logs?service=api&lines=50" | sed '$d')"
[[ "$(jq -r .count <<<"$logs")" -gt 0 ]] || fail "Keine Protokollzeilen"

log "Stoppen → Wartungsseite, Starten, Neustart als Massenaktion"
action stop
wait_proxy /health/ready 503
grep -q 'Vorübergehend nicht erreichbar' /tmp/proxy-body || fail "Wartungsseite fehlt"
action start
wait_proxy /health/ready 200
result="$(api POST /api/tenants/bulk "{\"tenantIds\":[\"$TENANT\"],\"action\":\"restart\"}")"
expect_status 200 "$result" "Massenaktion"
[[ "$(body "$result" | jq -r '.queued | length')" == "1" ]] || fail "Massenaktion nicht eingereiht"
wait_operation "$(api GET "/api/tenants/$TENANT/operations" | sed '$d' | jq -r '.[0].id')" 600
wait_proxy /health/ready 200

log "Sperren (Zahlungsverzug) und Entsperren"
action lock '{"reason":"Zahlungsverzug","stopInstance":false}'
wait_proxy / 503
action unlock
wait_proxy /health/ready 200

log "Sicherung"
action backup '{}' 900
detail="$(api POST "/api/tenants/$TENANT/refresh" | sed '$d')"
jq -e '.backup.lastSuccess != null' <<<"$detail" >/dev/null || fail "Sicherungsstatus fehlt: $(jq .backup <<<"$detail")"
[[ "$(api GET "/api/tenants/$TENANT/snapshots" | sed '$d' | jq -r '.items | length')" -ge 1 ]] || fail "Keine Sicherung in der Liste"

log "Aktualisierung $SUITE_VERSION → $SUITE_VERSION_NEXT mit Vorab-Sicherung, danach Rücknahme"
action update "{\"version\":\"$SUITE_VERSION_NEXT\",\"backupFirst\":true}" 1200
wait_proxy /health/ready 200
docker ps --filter "label=com.docker.compose.project=estrichone-$SLUG" --format '{{.Image}}' | grep "estrichone-api:$SUITE_VERSION_NEXT" >/dev/null \
  || fail "API läuft nicht mit $SUITE_VERSION_NEXT"
snapshots="$(api GET "/api/tenants/$TENANT/snapshots" | sed '$d')"
[[ "$(jq -r .previousVersion <<<"$snapshots")" == "$SUITE_VERSION" ]] || fail "Vorherige Version nicht vermerkt"
[[ "$(jq -r .rollbackSnapshot <<<"$snapshots")" =~ ^[0-9a-f]{64}$ ]] || fail "Snapshot der Vorab-Sicherung fehlt: $snapshots"
action rollback '{"confirm":true}' 1200
wait_proxy /health/ready 200
docker ps --filter "label=com.docker.compose.project=estrichone-$SLUG" --format '{{.Image}}' | grep "estrichone-api:$SUITE_VERSION\$" >/dev/null \
  || fail "API läuft nach der Rücknahme nicht mit $SUITE_VERSION"
suite_login petra "$SUITE_PASSWORD"

log "Wiederherstellung der letzten Sicherung"
action restore '{"snapshot":"latest","confirm":true,"backupFirst":false}' 1200
wait_proxy /health/ready 200
suite_login petra "$SUITE_PASSWORD"

log "Export (Sicherungs-Repository + Schlüssel)"
result="$(api POST "/api/tenants/$TENANT/export" '{"password":"ci-sehr-geheimes-passwort"}')"
expect_status 200 "$result" "Export"
curl -sS -b "$JAR" -o /tmp/export.tar.gz "$CONSOLE$(body "$result" | jq -r .url)"
tar -tzf /tmp/export.tar.gz | grep '^instanz.env$' >/dev/null || fail "Export ohne instanz.env"
tar -tzf /tmp/export.tar.gz | grep '^backup-repository/config$' >/dev/null || fail "Export ohne Sicherungs-Repository"

log "Löschen mit Endsicherung (doppelte Bestätigung)"
result="$(api POST "/api/tenants/$TENANT/actions/delete" '{"confirm":true,"confirmSlug":"falsch"}')"
expect_status 400 "$result" "Löschen ohne korrekte Bestätigung"
action delete "{\"confirm\":true,\"confirmSlug\":\"$SLUG\",\"finalBackup\":true}" 900
[[ -z "$(docker ps -aq --filter "label=com.docker.compose.project=estrichone-$SLUG")" ]] || fail "Container sind nicht entfernt"
! docker network inspect "estrichone-$SLUG-edge" >/dev/null 2>&1 || fail "Edge-Netz ist nicht entfernt"
sudo test ! -d "$ROOT/instances/$SLUG" || fail "Instanzverzeichnis ist nicht gelöscht"
sudo sh -c "ls -d $ROOT/archive/$SLUG-*/backup-repository && test -f $ROOT/archive/$SLUG-*/instanz.env" || fail "Endsicherung fehlt im Archiv"
[[ "$(api GET /api/archives | sed '$d' | jq -r '.[0].slug')" == "$SLUG" ]] || fail "Archiv nicht in der Konsole gelistet"

log "Ereignisprotokoll"
api GET "/api/audit?q=$SLUG&take=100" | sed '$d' | jq -r '.items[] | "\(.at)  \(.actor)  \(.action)  \(.success)"'

log "Reparatur: Installation erneut ausführen (idempotent), Notfall-Zurücksetzen des Admins"
sudo env HOSTING_NONINTERACTIVE=1 HOSTING_PULL=never estrichone-hosting install | tee /tmp/repair.log
grep -q "Vorhandene Installation gefunden" /tmp/repair.log || fail "Reparatur-Lauf hat die Installation nicht erkannt"
! grep -q "Startpasswort:" /tmp/repair.log || fail "Reparatur hat ein neues Konto angelegt"
expect_status 200 "$(api GET /api/setup-wizard)" "Sitzung nach Reparatur"
sudo env HOSTING_NONINTERACTIVE=1 HOSTING_PULL=never estrichone-hosting reset-admin chefin | tee /tmp/reset.log
NEW_PASSWORD="$(sed -n 's/^  Startpasswort: \([A-Za-z0-9-]*\) .*$/\1/p' /tmp/reset.log)"
[[ -n "$NEW_PASSWORD" ]] || fail "reset-admin ohne neues Passwort"
expect_status 401 "$(api GET /api/setup-wizard)" "Alte Sitzung nach reset-admin"
CSRF=""
expect_status 204 "$(api POST /api/auth/login "{\"userName\":\"chefin\",\"password\":\"$NEW_PASSWORD\"}")" "Anmeldung nach reset-admin"

log "Deinstallation (--purge)"
sudo env HOSTING_NONINTERACTIVE=1 HOSTING_PULL=never HOSTING_PURGE_CONFIRM=LÖSCHEN estrichone-hosting uninstall --purge
[[ -z "$(docker ps -aq --filter name=estrichone-hosting)" ]] || fail "Container der Konsole sind nicht entfernt"
[[ -z "$(sudo ls -A "$ROOT")" ]] || fail "Datenverzeichnis ist nicht leer"
[[ ! -e /usr/local/bin/estrichone-hosting ]] || fail "Verwaltungsbefehl nicht entfernt"
echo
echo "Integrationstest erfolgreich."
