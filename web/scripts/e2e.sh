#!/usr/bin/env bash
# Ende-zu-Ende-Tests der Web-Oberfläche gegen einen echten Server:
# PostgreSQL (Docker) + API + Vite-Entwicklungsserver mit Proxy, dann Playwright.
# VITE überschreibt den Aufruf von Vite (z. B. in einem Container mit neuerem Node.js).
# WEB_IMAGE=estrichone/web:… testet stattdessen das fertige Image (Caddy, HTTPS, CSP) wie im Betrieb.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
API_PORT=5080
DB_PORT="${DB_PORT:-55433}"
DB_CONTAINER="estrichone-web-e2e-db"
WORK="$(mktemp -d)"
PIDS=()
CLEANUP_CONTAINER=""

cleanup() {
  local status=$?
  if [[ $status -ne 0 && -f "$WORK/api.log" ]]; then
    echo "Fehlgeschlagen (Status $status). Letzte Zeilen des Server-Protokolls:" >&2
    tail -n 30 "$WORK/api.log" >&2 || true
  fi
  for pid in "${PIDS[@]}"; do kill "$pid" 2>/dev/null || true; done
  if [[ -n "$CLEANUP_CONTAINER" ]]; then docker rm -f "$CLEANUP_CONTAINER" >/dev/null 2>&1 || true; fi
  docker rm -f "$DB_CONTAINER" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

docker rm -f "$DB_CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$DB_CONTAINER" -p "$DB_PORT:5432" \
  -e POSTGRES_USER=estrichone -e POSTGRES_PASSWORD=estrichone -e POSTGRES_DB=estrichone postgres:17-alpine >/dev/null
until docker exec "$DB_CONTAINER" pg_isready -U estrichone -d estrichone >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Default="Host=localhost;Port=$DB_PORT;Database=estrichone;Username=estrichone;Password=estrichone"
export ASPNETCORE_URLS="http://localhost:$API_PORT"
export EstrichOne__PublicUrl="${WEB_IMAGE:+https://localhost:8443}"
[[ -n "$EstrichOne__PublicUrl" ]] || EstrichOne__PublicUrl="http://localhost:4200"
export EstrichOne__ServerName="Estrichbau E2E GmbH"
export EstrichOne__Files__RootPath="$WORK/files"
export Serilog__MinimumLevel__Default=Warning
dotnet publish "$ROOT/backend/src/EstrichOne.Api" -c Release -o "$WORK/api" --nologo -v quiet
dotnet "$WORK/api/EstrichOne.Api.dll" migrate >/dev/null
dotnet "$WORK/api/EstrichOne.Api.dll" >"$WORK/api.log" 2>&1 &
PIDS+=($!)
for _ in $(seq 1 60); do
  curl -sf --noproxy localhost "http://localhost:$API_PORT/api/v1/server-info" >/dev/null && break
  sleep 1
done
E2E_SETUP_CODE="$(grep -o 'Einrichtungscode: [A-Z0-9-]*' "$WORK/api.log" | tail -n 1 | awk '{print $2}')"
export E2E_SETUP_CODE

cd "$ROOT/web"
if [[ -n "${WEB_IMAGE:-}" ]]; then
  # Gleiches Caddyfile wie im Betrieb, nur mit lokaler API-Adresse.
  sed "s/api:8080/localhost:$API_PORT/g" "$ROOT/deploy/Caddyfile" >"$WORK/Caddyfile"
  docker rm -f estrichone-web-e2e >/dev/null 2>&1 || true
  docker run -d --name estrichone-web-e2e --network host -e ESTRICHONE_DOMAIN=localhost:8443 \
    -v "$WORK/Caddyfile:/etc/caddy/Caddyfile:ro" "$WEB_IMAGE" >/dev/null
  CLEANUP_CONTAINER=estrichone-web-e2e
  export E2E_BASE_URL=https://localhost:8443
  for _ in $(seq 1 60); do
    curl -skf --noproxy localhost "$E2E_BASE_URL/" >/dev/null && break
    sleep 1
  done
else
  ${VITE:-npx vite} --port 4200 --strictPort >"$WORK/web.log" 2>&1 &
  PIDS+=($!)
  for _ in $(seq 1 120); do
    curl -sf --noproxy localhost http://localhost:4200/ >/dev/null && break
    sleep 1
  done
fi

npx playwright test "$@"
