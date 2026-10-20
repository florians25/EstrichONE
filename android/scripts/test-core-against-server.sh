#!/usr/bin/env bash
# Führt die Tests von android/core gegen einen echten EstrichONE-Server aus (wie ios/scripts):
# startet PostgreSQL (Docker) und die API, schließt die Ersteinrichtung ab und ruft `gradle :core:test` auf.
# GRADLE: Befehl für Gradle (Standard: ./gradlew, sonst gradle).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PORT="${PORT:-5082}"
DB_PORT="${DB_PORT:-55433}"
DB_CONTAINER="estrichone-android-live-db"
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
# Viele Anmeldungen in kurzer Zeit (je Test ein Gerät): Bremsen nur hier anheben.
export EstrichOne__Authentication__RequestsPerMinutePerAddress=500
export EstrichOne__Devices__RegistrationsPerMinutePerAddress=500
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

echo "== gradle :core:test gegen http://localhost:$PORT"
cd "$ROOT/android"
GRADLE="${GRADLE:-$( [[ -x ./gradlew ]] && echo ./gradlew || echo gradle )}"
export ESTRICHONE_LIVE_SERVER="http://localhost:$PORT" ESTRICHONE_LIVE_LOGIN=admin ESTRICHONE_LIVE_PASSWORD="$PASSWORD"
$GRADLE :core:test --rerun-tasks --no-daemon 2>&1 | tee "$WORK/gradle.log"
test "${PIPESTATUS[0]}" -eq 0
# Die Live-Tests dürfen nicht stillschweigend übersprungen worden sein.
test "$(grep -c 'Echter Server.*PASSED' "$WORK/gradle.log")" -eq 2
