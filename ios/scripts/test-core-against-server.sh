#!/usr/bin/env bash
# Führt die Tests von EstrichCore gegen einen echten EstrichONE-Server aus (ADR 0012):
# startet PostgreSQL (Docker) und die API, schließt die Ersteinrichtung ab und ruft `swift test` im
# Container swift:6.1-noble auf (SWIFT_IMAGE; fehlende SQLite-Header werden nachinstalliert).
# Zusätzliche docker-run-Argumente (z. B. Proxy) über SWIFT_DOCKER_ARGS.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PORT="${PORT:-5081}"
DB_PORT="${DB_PORT:-55432}"
DB_CONTAINER="estrichone-core-live-db"
WORK="$(mktemp -d)"
PASSWORD="Integration-Test-2026!"
API_PID=""

cleanup() {
  local status=$?
  if [[ $status -ne 0 && -f "$WORK/api.log" ]]; then
    echo "Fehlgeschlagen (Status $status). Letzte Zeilen des Server-Protokolls:" >&2
    tail -n 40 "$WORK/api.log" >&2 || true
  fi
  if [[ -n "$API_PID" ]]; then kill "$API_PID" 2>/dev/null || true; fi
  docker rm -f "$DB_CONTAINER" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

echo "== PostgreSQL starten"
docker rm -f "$DB_CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$DB_CONTAINER" -p "$DB_PORT:5432" \
  -e POSTGRES_USER=estrichone -e POSTGRES_PASSWORD=estrichone -e POSTGRES_DB=estrichone postgres:17-alpine >/dev/null
until docker exec "$DB_CONTAINER" pg_isready -U estrichone -d estrichone >/dev/null 2>&1; do sleep 1; done

echo "== API bauen, migrieren, starten"
export ConnectionStrings__Default="Host=localhost;Port=$DB_PORT;Database=estrichone;Username=estrichone;Password=estrichone"
export ASPNETCORE_URLS="http://localhost:$PORT"
export EstrichOne__PublicUrl="http://localhost:$PORT"
export EstrichOne__ServerName="EstrichONE Integrationstest"
export EstrichOne__Files__RootPath="$WORK/files"
dotnet publish "$ROOT/backend/src/EstrichOne.Api" -c Release -o "$WORK/api" --nologo -v quiet
dotnet "$WORK/api/EstrichOne.Api.dll" migrate >/dev/null
dotnet "$WORK/api/EstrichOne.Api.dll" >"$WORK/api.log" 2>&1 &
API_PID=$!
for _ in $(seq 1 60); do
  curl -sf --noproxy localhost "http://localhost:$PORT/api/v1/server-info" >/dev/null && break
  sleep 1
done

echo "== Ersteinrichtung"
CODE="$(grep -o 'Einrichtungscode: [A-Z0-9-]*' "$WORK/api.log" | tail -n 1 | awk '{print $2}')"
curl -sf --noproxy localhost -H 'Content-Type: application/json' -o /dev/null "http://localhost:$PORT/api/v1/setup" \
  -d "{\"setupCode\":\"$CODE\",\"companyName\":\"Integrationstest\",\"administrator\":{\"displayName\":\"Admin\",\"userName\":\"admin\",\"email\":null,\"password\":\"$PASSWORD\"}}"

echo "== swift test gegen http://localhost:$PORT"
# shellcheck disable=SC2086
docker run --rm --network host ${SWIFT_DOCKER_ARGS:-} \
  -e ESTRICHONE_LIVE_SERVER="http://localhost:$PORT" -e ESTRICHONE_LIVE_LOGIN=admin -e ESTRICHONE_LIVE_PASSWORD="$PASSWORD" \
  -v "$ROOT":/repo -w /repo/ios/Packages/EstrichCore "${SWIFT_IMAGE:-swift:6.1-noble}" \
  bash -c 'test -f /usr/include/sqlite3.h || (apt-get update -qq && apt-get install -y -qq libsqlite3-dev >/dev/null)
           swift test 2>&1 | tee /tmp/swift-test.log; test "${PIPESTATUS[0]}" -eq 0 && grep -q "Echter Server\" passed" /tmp/swift-test.log'
