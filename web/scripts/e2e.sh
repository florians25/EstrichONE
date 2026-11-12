#!/usr/bin/env bash
# Ende-zu-Ende-Tests der Web-Oberfläche gegen einen echten Server:
# PostgreSQL (Docker) + API + UI5-Entwicklungsserver (ui5 serve mit Proxy auf die API), dann Playwright.
# UI5_SERVE überschreibt den Aufruf des Entwicklungsservers (z. B. in einem Container mit anderem Node.js).
# WEB_IMAGE=estrichone/web:… testet stattdessen das fertige Image (Caddy, HTTPS, CSP) wie im Betrieb.
# Zusätzlich E2E_PROXY=nginx: Caddy in der Betriebsart „proxy“ hinter Nginx mit client_max_body_size 10m
# (wie Nginx Proxy Manager mit knapper Größengrenze).
# E2E_PROXY=npm: Caddy „proxy“ hinter Nginx mit der Konfiguration, die Nginx Proxy Manager für einen Proxy Host erzeugt
# (TLS mit eigenem Zertifikat, Force SSL, HTTP/2, HSTS, Block Common Exploits, Websockets, erweiterte Einstellungen)
# unter https://app.estrichone.de:8443 – der Server ist absichtlich auf eine andere Domain eingestellt (Fehlerbild 0.1.0).
# Aufruf: E2E_PROXY=npm WEB_IMAGE=… scripts/e2e.sh --config=playwright.proxy.config.ts
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
  # shellcheck disable=SC2086 # mehrere Containernamen
  if [[ -n "$CLEANUP_CONTAINER" ]]; then docker rm -f $CLEANUP_CONTAINER >/dev/null 2>&1 || true; fi
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
if [[ -n "${WEB_IMAGE:-}" && "${E2E_PROXY:-}" == "nginx" ]]; then EstrichOne__PublicUrl="http://localhost:8089"; fi
if [[ -n "${WEB_IMAGE:-}" && "${E2E_PROXY:-}" == "npm" ]]; then EstrichOne__PublicUrl="https://estrichone.deinefirma.de"; export EstrichOne__TrustForwardedHeaders=true; fi
[[ -n "$EstrichOne__PublicUrl" ]] || EstrichOne__PublicUrl="http://localhost:4200"
export EstrichOne__ServerName="Estrichbau E2E GmbH"
export EstrichOne__Files__RootPath="$WORK/files"
export Serilog__MinimumLevel__Default=Warning
# Systemhinweise wie von der Hosting-Konsole setzen (Instanz-Schlüssel nur für den Test).
export EstrichOne__Notices__ManagementToken="e2e-notices-token"
export E2E_NOTICES_TOKEN="$EstrichOne__Notices__ManagementToken"
# Update-Ablauf: Statusdatei wie von update.sh (21-update-ablauf.spec.ts schreibt sie).
export EstrichOne__Notices__UpdateStatePath="$WORK/update-maintenance.json"
export E2E_UPDATE_STATE="$EstrichOne__Notices__UpdateStatePath"
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

# Archivserver (backend/src/EstrichOne.Archive) für die Seite System → Archiv: koppeln per Adresse und Code.
dotnet publish "$ROOT/backend/src/EstrichOne.Archive" -c Release -o "$WORK/archive" --nologo -v quiet
ESTRICHONE_DATA="$WORK/archive-data" ESTRICHONE_PORT=18443 ESTRICHONE_NAME="Archiv E2E" dotnet "$WORK/archive/estrichone-archive.dll" >"$WORK/archive.log" 2>&1 &
PIDS+=($!)
for _ in $(seq 1 60); do grep -q 'KOPPLUNGSCODE' "$WORK/archive.log" && break; sleep 1; done
E2E_ARCHIVE_CODE="$(sed -n 's/.*KOPPLUNGSCODE: *\([A-Z0-9-]*\).*/\1/p' "$WORK/archive.log" | tail -n 1)"
export E2E_ARCHIVE_CODE E2E_ARCHIVE_ADDRESS="127.0.0.1:18443"

# Storage der Speicher-Auslagerung (gleicher Dienst, Rolle offload) für die Seite System → Speicher-Auslagerung.
ESTRICHONE_ROLE=offload ESTRICHONE_DATA="$WORK/offload-data" ESTRICHONE_PORT=18445 ESTRICHONE_NAME="Storage E2E" \
  dotnet "$WORK/archive/estrichone-archive.dll" >"$WORK/offload.log" 2>&1 &
PIDS+=($!)
for _ in $(seq 1 60); do grep -q 'KOPPLUNGSCODE' "$WORK/offload.log" && break; sleep 1; done
E2E_OFFLOAD_CODE="$(sed -n 's/.*KOPPLUNGSCODE: *\([A-Z0-9-]*\).*/\1/p' "$WORK/offload.log" | tail -n 1)"
export E2E_OFFLOAD_CODE E2E_OFFLOAD_ADDRESS="127.0.0.1:18445"

cd "$ROOT/web"
if [[ -n "${WEB_IMAGE:-}" ]]; then
  # Gleiches Caddyfile wie im Betrieb, nur mit lokaler API-Adresse.
  sed "s/api:8080/localhost:$API_PORT/g" "$ROOT/deploy/Caddyfile" >"$WORK/Caddyfile"
  docker rm -f estrichone-web-e2e estrichone-proxy-e2e >/dev/null 2>&1 || true
  if [[ "${E2E_PROXY:-}" == "npm" ]]; then
    # Wie der Docker-Installer mit -e ESTRICHONE_MODE=proxy -e ESTRICHONE_HTTP_PORT=… (compose.proxy.yml: private Netze vertraut).
    docker run -d --name estrichone-web-e2e --network host -e ESTRICHONE_MODE=proxy -e ESTRICHONE_HTTP_PORT=8088 \
      -e ESTRICHONE_DOMAIN=estrichone.deinefirma.de -e ESTRICHONE_TRUSTED_PROXIES=private_ranges \
      -v "$WORK/Caddyfile:/etc/caddy/Caddyfile:ro" "$WEB_IMAGE" >/dev/null
    openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=app.estrichone.de" -addext "subjectAltName=DNS:app.estrichone.de" \
      -keyout "$WORK/privkey.pem" -out "$WORK/fullchain.pem" 2>/dev/null
    chmod 644 "$WORK/privkey.pem"
    # Aufbau wie /data/nginx/proxy_host/1.conf von Nginx Proxy Manager (Vorlagen proxy_host.conf, proxy.conf,
    # force-ssl.conf, block-exploits.conf) inkl. der erweiterten Einstellungen aus der Anleitung (docs/installation).
    cat >"$WORK/nginx.conf" <<'NGINX'
events {}
http {
  proxy_http_version 1.1;
  proxy_set_header X-Forwarded-Scheme $scheme;
  proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
  proxy_set_header Accept-Encoding "";
  client_max_body_size 2000m;
  map $http_x_forwarded_proto $x_forwarded_proto { "http" "http"; "https" "https"; default $scheme; }
  map $http_x_forwarded_scheme $x_forwarded_scheme { "http" "http"; "https" "https"; default $scheme; }
  map $scheme $hsts_header { https "max-age=63072000; preload"; }
  server {
    set $forward_scheme http;
    set $server "127.0.0.1";
    set $port 8088;
    listen 8080;
    listen 8443 ssl;
    server_name app.estrichone.de;
    http2 on;
    ssl_certificate /etc/nginx/fullchain.pem;
    ssl_certificate_key /etc/nginx/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    # Block Common Exploits (Auszug aus block-exploits.conf)
    set $block_file_injections 0;
    if ($query_string ~ "[a-zA-Z0-9_]=http://") { set $block_file_injections 1; }
    if ($query_string ~ "[a-zA-Z0-9_]=(\.\.//?)+") { set $block_file_injections 1; }
    if ($query_string ~ "[a-zA-Z0-9_]=/([a-z0-9_.]//?)+") { set $block_file_injections 1; }
    if ($block_file_injections = 1) { return 403; }
    if ($query_string ~ "(<|%3C).*script.*(>|%3E)") { return 403; }
    add_header Strict-Transport-Security $hsts_header always;
    # Force SSL
    if ($scheme = "http") { return 301 https://$host$request_uri; }
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection $http_connection;
    proxy_http_version 1.1;
    # Erweitert (Anleitung): große Uploads, Echtzeit ohne Puffer
    client_max_body_size 600m;
    proxy_request_buffering off;
    proxy_buffering off;
    proxy_read_timeout 1h;
    proxy_send_timeout 1h;
    location / {
      add_header Strict-Transport-Security $hsts_header always;
      proxy_set_header Upgrade $http_upgrade;
      proxy_set_header Connection $http_connection;
      proxy_http_version 1.1;
      add_header X-Served-By $host;
      proxy_set_header Host $host;
      proxy_set_header X-Forwarded-Scheme $x_forwarded_scheme;
      proxy_set_header X-Forwarded-Proto $x_forwarded_proto;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header X-Real-IP $remote_addr;
      proxy_pass $forward_scheme://$server:$port$request_uri;
    }
  }
}
NGINX
    docker run -d --name estrichone-proxy-e2e --network host -v "$WORK/nginx.conf:/etc/nginx/nginx.conf:ro" \
      -v "$WORK/fullchain.pem:/etc/nginx/fullchain.pem:ro" -v "$WORK/privkey.pem:/etc/nginx/privkey.pem:ro" nginx:1.27-alpine >/dev/null
    CLEANUP_CONTAINER="estrichone-web-e2e estrichone-proxy-e2e"
    export E2E_BASE_URL=https://app.estrichone.de:8443
    # Direktaufruf am Proxy vorbei über eine Nicht-Loopback-Adresse (Browser behandelt sie als unsicheren Ursprung).
    E2E_DIRECT_URL="http://$(hostname -I | awk '{print $1}'):8088"
    export E2E_DIRECT_URL
  elif [[ "${E2E_PROXY:-}" == "nginx" ]]; then
    docker run -d --name estrichone-web-e2e --network host -e ESTRICHONE_MODE=proxy -e ESTRICHONE_HTTP_PORT=8088 \
      -v "$WORK/Caddyfile:/etc/caddy/Caddyfile:ro" "$WEB_IMAGE" >/dev/null
    cat >"$WORK/nginx.conf" <<'NGINX'
events {}
http {
  client_max_body_size 10m;
  server {
    listen 8089;
    location / {
      proxy_pass http://127.0.0.1:8088;
      proxy_http_version 1.1;
      proxy_set_header Host $http_host;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto $scheme;
    }
  }
}
NGINX
    docker run -d --name estrichone-proxy-e2e --network host -v "$WORK/nginx.conf:/etc/nginx/nginx.conf:ro" nginx:1.27-alpine >/dev/null
    CLEANUP_CONTAINER="estrichone-web-e2e estrichone-proxy-e2e"
    export E2E_BASE_URL=http://localhost:8089
  else
    docker run -d --name estrichone-web-e2e --network host -e ESTRICHONE_DOMAIN=localhost -e ESTRICHONE_HTTPS_PORT=8443 -e ESTRICHONE_HTTP_PORT=8088 \
      -v "$WORK/Caddyfile:/etc/caddy/Caddyfile:ro" "$WEB_IMAGE" >/dev/null
    CLEANUP_CONTAINER=estrichone-web-e2e
    export E2E_BASE_URL=https://localhost:8443
  fi
  for _ in $(seq 1 60); do
    curl -skf --noproxy "*" --resolve app.estrichone.de:8443:127.0.0.1 "$E2E_BASE_URL/" >/dev/null && break
    sleep 1
  done
else
  ${UI5_SERVE:-npx ui5 serve} --port 4200 >"$WORK/web.log" 2>&1 &
  PIDS+=($!)
  for _ in $(seq 1 120); do
    curl -sf --noproxy localhost http://localhost:4200/ >/dev/null && break
    sleep 1
  done
fi

npx playwright test "$@"
