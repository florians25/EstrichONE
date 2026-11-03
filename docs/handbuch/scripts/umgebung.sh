#!/usr/bin/env bash
# Startet eine frische Demo-Instanz für die Handbuch-Bilder: PostgreSQL (Docker), API (dotnet), Backup-Dienst (Docker, restic)
# und den UI5-Build (web/dist) hinter server.mjs. Ports: Datenbank 55491, API 5191, Web 4291 (kollidiert nicht mit
# web/scripts/e2e.sh). Arbeitsordner $HB_WORK, Dateiablage $HB_FILES (Standard: /dev/shm, damit die Überwachung eine
# realistische Plattenbelegung zeigt). Version wie die neueste Versionsnotiz (docs/releases) oder $HB_VERSION.
# Docker Hub mit Ratenlimit: HB_MIRROR=mirror.gcr.io baut das Backup-Image über den Spiegel.
# Ausgabe: Einrichtungscode in $HB_WORK/setup-code. Beenden: umgebung.sh stop
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
HB_WORK="${HB_WORK:-${TMPDIR:-/tmp}/estrichone-handbuch}"
DEFAULT_FILES="$HB_WORK/files"; [[ -d /dev/shm && -w /dev/shm ]] && DEFAULT_FILES=/dev/shm/estrichone-handbuch/files
HB_FILES="${HB_FILES:-$DEFAULT_FILES}"
HB_VERSION="${HB_VERSION:-$(ls "$ROOT/docs/releases" | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+\.md$' | sed 's/^v//; s/\.md$//' | sort -V | tail -n 1)}"
DB=estrichone-handbuch-db
BACKUP=estrichone-handbuch-backup
RESTIC_PASSWORD=handbuch-demo-nur-lokal

stop() {
  for pid in "$HB_WORK"/*.pid; do [[ -f "$pid" ]] && kill "$(cat "$pid")" 2>/dev/null || true; done
  docker rm -f "$DB" "$BACKUP" >/dev/null 2>&1 || true
}
if [[ "${1:-}" == "stop" ]]; then stop; exit 0; fi
stop
# Dateien des Backup-Containers (Benutzer 1654) zuerst über Docker entfernen.
[[ -d "$HB_WORK" ]] && docker run --rm -v "$HB_WORK":/w alpine:3 sh -c 'rm -rf /w/repository /w/status /w/cache' >/dev/null 2>&1 || true
rm -rf "$HB_WORK"/{api,archive,repository,status,cache,config,*.log} "$HB_FILES" "$(dirname "$HB_FILES")/archive-data"
mkdir -p "$HB_WORK"/{repository,status,cache,config} "$HB_FILES"
chmod 777 "$HB_WORK"/{repository,status,cache} "$HB_FILES"
printf 'ESTRICHONE_DOMAIN=localhost\nESTRICHONE_VERSION=%s\n' "$HB_VERSION" >"$HB_WORK/config/estrichone.env"

docker run -d --name "$DB" -p 55491:5432 -e POSTGRES_USER=estrichone -e POSTGRES_PASSWORD=estrichone -e POSTGRES_DB=estrichone postgres:17-alpine >/dev/null
until docker exec "$DB" pg_isready -U estrichone -d estrichone >/dev/null 2>&1; do sleep 1; done
sleep 2
export ConnectionStrings__Default="Host=localhost;Port=55491;Database=estrichone;Username=estrichone;Password=estrichone"
export ASPNETCORE_URLS="http://localhost:5191"
export EstrichOne__PublicUrl="http://localhost:4291"
export EstrichOne__ServerName="Estrichbau Muster GmbH"
export EstrichOne__Files__RootPath="$HB_FILES"
export EstrichOne__Backup__StatusPath="$HB_WORK/status"
export EstrichOne__Backup__RepositoryPassword="$RESTIC_PASSWORD"
export EstrichOne__Backup__Repository=/repository
export EstrichOne__Backup__RepositoryPath="$HB_WORK/repository"
export EstrichOne__Backup__HostBackupDir=/opt/estrichone/backup-repository
export Serilog__MinimumLevel__Default=Warning
# Kein Abruf des Release-Feeds: Die Bilder sollen keinen Hinweis „Update verfügbar“ zeigen.
export EstrichOne__Updates__Enabled=false
dotnet publish "$ROOT/backend/src/EstrichOne.Api" -c Release -o "$HB_WORK/api" --nologo -v quiet -p:Version="$HB_VERSION"
dotnet "$HB_WORK/api/EstrichOne.Api.dll" migrate >/dev/null
nohup dotnet "$HB_WORK/api/EstrichOne.Api.dll" >"$HB_WORK/api.log" 2>&1 &
echo $! >"$HB_WORK/api.pid"
for _ in $(seq 1 90); do curl -sf --noproxy localhost http://localhost:5191/api/v1/server-info >/dev/null && break; sleep 1; done
grep -o 'Einrichtungscode: [A-Z0-9-]*' "$HB_WORK/api.log" | tail -n 1 | awk '{print $2}' >"$HB_WORK/setup-code"

# Archivserver (backend/src/EstrichOne.Archive) wie in web/scripts/e2e.sh; Kopplungscode für demodaten.py.
dotnet publish "$ROOT/backend/src/EstrichOne.Archive" -c Release -o "$HB_WORK/archive" --nologo -v quiet -p:Version="$HB_VERSION"
ESTRICHONE_DATA="$(dirname "$HB_FILES")/archive-data" ESTRICHONE_PORT=18491 ESTRICHONE_NAME="Archiv Büro Ergolding" \
  nohup dotnet "$HB_WORK/archive/estrichone-archive.dll" >"$HB_WORK/archive.log" 2>&1 &
echo $! >"$HB_WORK/archive.pid"
for _ in $(seq 1 60); do grep -q 'KOPPLUNGSCODE' "$HB_WORK/archive.log" && break; sleep 1; done
sed -n 's/.*KOPPLUNGSCODE: *\([A-Z0-9-]*\).*/\1/p' "$HB_WORK/archive.log" | tail -n 1 >"$HB_WORK/archive-code"

# Backup-Dienst wie in deploy/compose.yml (lokales Hauptziel); das Image wird bei Bedarf gebaut. Als root, weil die
# API hier nicht als Benutzer 1654 läuft und ihre Schlüsseldateien sonst nicht lesbar wären.
if ! docker image inspect estrichone/backup:handbuch >/dev/null 2>&1; then
  if [[ -n "${HB_MIRROR:-}" ]]; then
    # Über den Spiegel ohne Paketinstallation (curl/openssh braucht nur das Koppeln entfernter Ziele).
    sed "s#FROM restic/#FROM $HB_MIRROR/restic/#; s#FROM postgres:#FROM $HB_MIRROR/library/postgres:#; s#^RUN apk upgrade.*#RUN rm -f /usr/local/bin/gosu \&\& adduser -D -u 1654 -h /home/backup backup#; /^ && rm -f \/usr\/local\/bin\/gosu/d; /^ && adduser/d" \
      "$ROOT/docker/backup.Dockerfile" | docker build -q -t estrichone/backup:handbuch -f - "$ROOT" >/dev/null
  else
    docker build -q -t estrichone/backup:handbuch -f "$ROOT/docker/backup.Dockerfile" "$ROOT" >/dev/null
  fi
fi
docker run -d --name "$BACKUP" --network host -e PGHOST=localhost -e PGPORT=55491 -e PGDATABASE=estrichone -e PGUSER=estrichone \
  -e PGPASSWORD=estrichone -e RESTIC_PASSWORD="$RESTIC_PASSWORD" -e RESTIC_CACHE_DIR=/cache -e BACKUP_REPOSITORY=/repository -e BACKUP_TIME=22:30 \
  -v "$HB_FILES":/data/files -v "$HB_WORK/config":/config:ro -v "$HB_WORK/status":/status -v "$HB_WORK/cache":/cache \
  -v "$HB_WORK/repository":/repository --tmpfs /tmp -u 0:0 estrichone/backup:handbuch schedule >/dev/null

if [[ ! -f "$ROOT/web/dist/index.html" || -n "${HB_REBUILD:-}" ]]; then (cd "$ROOT/web" && npm run build >/dev/null); fi
nohup node "$ROOT/docs/handbuch/scripts/server.mjs" "$ROOT/web/dist" 4291 5191 >"$HB_WORK/web.log" 2>&1 &
echo $! >"$HB_WORK/web.pid"
for _ in $(seq 1 30); do curl -sf --noproxy localhost http://localhost:4291/anmelden >/dev/null && break; sleep 1; done
echo "Bereit: http://localhost:4291 (Version $HB_VERSION, Einrichtungscode $(cat "$HB_WORK/setup-code"))"
