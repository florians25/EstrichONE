#!/usr/bin/env bash
# Echter Integrationstest der Hosting-Konsole (CI: Linux-Runner mit Docker):
# Installer ausführen → Konsole + Caddy laufen → Mandant mit den veröffentlichten Suite-Images bereitstellen →
# Health über den gemeinsamen Proxy prüfen → stoppen/starten, sperren/entsperren, sichern → löschen mit Endsicherung.
#   IMAGE=estrichone/hosting:ci SUITE_VERSION=0.1.0-preview.4 hosting/scripts/integration.sh
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112
set -euo pipefail

IMAGE="${IMAGE:-estrichone/hosting:ci}"
SUITE_VERSION="${SUITE_VERSION:-0.1.0-preview.4}"
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
proxy() { # proxy PFAD → HTTP-Status über den gemeinsamen Caddy
  curl -s -o /tmp/proxy-body -w '%{http_code}' -H "Host: $DOMAIN" "http://localhost:$HTTP_PORT$1"
}
wait_proxy() { # wait_proxy PFAD STATUS
  for _ in $(seq 1 60); do [[ "$(proxy "$1")" == "$2" ]] && return 0; sleep 2; done
  fail "Proxy liefert für $1 nicht HTTP $2 (zuletzt $(proxy "$1"): $(head -c 300 /tmp/proxy-body))"
}

log "Installer ausführen ($IMAGE)"
sudo mkdir -p "$ROOT"
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$ROOT:$ROOT" \
  -e HOSTING_ROOT="$ROOT" -e HOSTING_IMAGE="$IMAGE" -e HOSTING_BASE_DOMAIN=hosting.test -e HOSTING_CONSOLE_DOMAIN=konsole.hosting.test \
  -e HOSTING_PROXY=caddy -e HOSTING_TLS=off -e HOSTING_HTTP_PORT="$HTTP_PORT" -e HOSTING_HTTPS_PORT=8443 \
  -e HOSTING_DEFAULT_VERSION="$SUITE_VERSION" "$IMAGE" install | tee /tmp/install.log
CODE="$(grep -oE '^ +[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$' /tmp/install.log | tr -d ' ' | tail -n1)"
[[ -n "$CODE" ]] || fail "Installer hat keinen Einrichtungscode ausgegeben"
sudo test -f "$ROOT/.env" || fail ".env fehlt"
[[ "$(sudo stat -c %a "$ROOT/.env")" == "600" ]] || fail ".env ist nicht nur für root lesbar"

log "Ersteinrichtung und Anmeldung"
result="$(api POST /api/auth/setup "{\"setupCode\":\"$CODE\",\"userName\":\"admin\",\"password\":\"ci-sehr-geheimes-passwort\"}")"
expect_status 204 "$result" "Ersteinrichtung"
CSRF="$(api GET /api/auth/state | sed '$d' | jq -r .csrfToken)"
[[ "$CSRF" != "null" && -n "$CSRF" ]] || fail "Keine Sitzung nach der Ersteinrichtung"
[[ "$(curl -s -o /dev/null -w '%{http_code}' -H 'Host: konsole.hosting.test' "http://localhost:$HTTP_PORT/health")" == "200" ]] \
  || fail "Konsole über den Proxy nicht erreichbar"

log "Mandant anlegen und bereitstellen (Suite $SUITE_VERSION aus der Registry)"
PLAN="$(api GET /api/plans | sed '$d' | jq -r '.[0].id')"
result="$(api POST /api/tenants "{\"companyName\":\"CI Estrich GmbH\",\"customerNumber\":\"CI-1\",\"contactEmail\":\"ci@example.org\",\"slug\":\"$SLUG\",\"planId\":\"$PLAN\",\"version\":\"$SUITE_VERSION\"}")"
expect_status 201 "$result" "Mandant anlegen"
TENANT="$(body "$result" | jq -r .id)"
wait_operation "$(body "$result" | jq -r .openOperation.id)" 1200
sudo test -f "$ROOT/instances/$SLUG/.env" || fail "Instanzverzeichnis fehlt"
[[ -z "$(docker ps --filter "label=com.docker.compose.project=estrichone-$SLUG" --format '{{.Ports}}' | grep -E '0\.0\.0\.0|:::' || true)" ]] \
  || fail "Instanz veröffentlicht Ports"
[[ "$(sudo stat -c %u "$ROOT/instances/$SLUG/data/files")" == "1654" ]] || fail "Datenverzeichnis gehört nicht UID 1654"

log "Health über den gemeinsamen Proxy"
wait_proxy /health/ready 200
[[ "$(proxy /api/v1/server-info)" == "200" ]] || fail "server-info nicht erreichbar"
jq -e '.setupRequired == true and .serverVersion != null' /tmp/proxy-body >/dev/null || fail "server-info unerwartet: $(cat /tmp/proxy-body)"
detail="$(api POST "/api/tenants/$TENANT/refresh" | sed '$d')"
echo "$detail" | jq '{health, setupRequired, setupCode, runningVersion, problems, containers: [.containers[] | {service, state, health}]}'
[[ "$(jq -r .health <<<"$detail")" == "Healthy" ]] || fail "Instanz nicht gesund"
jq -e '.setupCode | test("^[A-Z0-9]{4}(-[A-Z0-9]{4})+$")' <<<"$detail" >/dev/null || fail "Kein Einrichtungscode erkannt"
logs="$(api GET "/api/tenants/$TENANT/logs?service=api&lines=50" | sed '$d')"
[[ "$(jq -r .count <<<"$logs")" -gt 0 ]] || fail "Keine Protokollzeilen"

log "Stoppen → Wartungsseite, Starten"
action stop
wait_proxy /health/ready 503
grep -q 'Vorübergehend nicht erreichbar' /tmp/proxy-body || fail "Wartungsseite fehlt"
action start
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

log "Löschen mit Endsicherung (doppelte Bestätigung)"
result="$(api POST "/api/tenants/$TENANT/actions/delete" '{"confirm":true,"confirmSlug":"falsch"}')"
expect_status 400 "$result" "Löschen ohne korrekte Bestätigung"
action delete "{\"confirm\":true,\"confirmSlug\":\"$SLUG\",\"finalBackup\":true}" 900
[[ -z "$(docker ps -aq --filter "label=com.docker.compose.project=estrichone-$SLUG")" ]] || fail "Container sind nicht entfernt"
! docker network inspect "estrichone-$SLUG-edge" >/dev/null 2>&1 || fail "Edge-Netz ist nicht entfernt"
sudo test ! -d "$ROOT/instances/$SLUG" || fail "Instanzverzeichnis ist nicht gelöscht"
sudo sh -c "ls -d $ROOT/archive/$SLUG-*/backup-repository && test -f $ROOT/archive/$SLUG-*/instanz.env" || fail "Endsicherung fehlt im Archiv"

log "Ereignisprotokoll"
api GET "/api/audit?q=$SLUG&take=100" | sed '$d' | jq -r '.items[] | "\(.at)  \(.actor)  \(.action)  \(.success)"'
echo
echo "Integrationstest erfolgreich."
