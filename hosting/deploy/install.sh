#!/bin/sh
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112,SC1091
# EstrichONE Hosting-Konsole – Installation und Aktualisierung mit einem Docker-Befehl:
#
#   docker run --rm -it -v /var/run/docker.sock:/var/run/docker.sock -v /srv/estrichone-hosting:/srv/estrichone-hosting \
#     ghcr.io/florians25/estrichone-hosting:latest install
#
# Ohne Rückfragen:  -e HOSTING_BASE_DOMAIN=estrichone-hosting.de -e HOSTING_ACME_EMAIL=admin@firma.de
# Weitere Optionen: -e HOSTING_CONSOLE_DOMAIN=konsole.… -e HOSTING_TLS=acme|internal|off -e HOSTING_PROXY=caddy|extern
#                   -e HOSTING_HTTP_PORT=80 -e HOSTING_HTTPS_PORT=443 -e HOSTING_ROOT=/srv/estrichone-hosting
# Erneut ausführen = Konsole auf die Version dieses Images aktualisieren; .env und alle Mandanten bleiben erhalten.
set -eu

ROOT="${HOSTING_ROOT:-/srv/estrichone-hosting}"
VERSION="${HOSTING_IMAGE_VERSION:-latest}"
REGISTRY="${ESTRICHONE_REGISTRY:-ghcr.io/florians25}"
IMAGE="${HOSTING_IMAGE:-$REGISTRY/estrichone-hosting:$VERSION}"

fail() { echo "FEHLER: $*" >&2; exit 1; }
ask() { # ask VAR "Frage" "Standard"
  eval "current=\${$1:-}"
  if [ -z "$current" ] && [ -t 0 ]; then
    printf '%s [%s]: ' "$2" "$3"; read -r current || true
  fi
  eval "$1=\"\${current:-$3}\""
}
set_env() { # set_env SCHLÜSSEL WERT
  if grep -q "^$1=" "$ROOT/.env"; then sed -i "s|^$1=.*|$1=$2|" "$ROOT/.env"; else echo "$1=$2" >> "$ROOT/.env"; fi
}
valid_domain() { echo "$1" | grep -Eq '^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9-]{2,63}$'; }

[ -S /var/run/docker.sock ] || fail "Docker-Socket fehlt: bitte mit -v /var/run/docker.sock:/var/run/docker.sock starten."
docker version >/dev/null 2>&1 || fail "Docker ist über den Socket nicht erreichbar."
if ! { [ -d "$ROOT" ] && touch "$ROOT/.schreibtest" 2>/dev/null; }; then
  fail "Bitte das Verzeichnis einbinden: -v $ROOT:$ROOT (gleicher Pfad innen und außen)."
fi
rm -f "$ROOT/.schreibtest"

echo "EstrichONE Hosting-Konsole $VERSION – Installation nach $ROOT"
cp /opt/estrichone-hosting/compose.yml "$ROOT/compose.yml"

if [ -f "$ROOT/.env" ]; then
  echo "Vorhandene Installation gefunden – Konfiguration bleibt, Konsole wird auf $IMAGE gesetzt."
  set_env HOSTING_IMAGE "$IMAGE"
  FIRST=0
else
  ask HOSTING_BASE_DOMAIN "Basis-Domain für Mandanten (Wildcard-DNS *.<domain> → dieser Server)" "estrichone-hosting.de"
  HOSTING_BASE_DOMAIN="$(echo "$HOSTING_BASE_DOMAIN" | tr '[:upper:]' '[:lower:]')"
  valid_domain "$HOSTING_BASE_DOMAIN" || fail "Ungültige Domain: $HOSTING_BASE_DOMAIN"
  ask HOSTING_CONSOLE_DOMAIN "Adresse der Konsole" "konsole.$HOSTING_BASE_DOMAIN"
  HOSTING_CONSOLE_DOMAIN="$(echo "$HOSTING_CONSOLE_DOMAIN" | tr '[:upper:]' '[:lower:]')"
  valid_domain "$HOSTING_CONSOLE_DOMAIN" || fail "Ungültige Domain: $HOSTING_CONSOLE_DOMAIN"
  ask HOSTING_PROXY "Reverse Proxy: caddy = mitgeliefert (Ports 80/443), extern = vorhandener Proxy (z. B. Nginx Proxy Manager)" "caddy"
  case "$HOSTING_PROXY" in caddy|extern) ;; *) fail "Reverse Proxy muss caddy oder extern sein." ;; esac
  if [ "$HOSTING_PROXY" = "caddy" ]; then
    ask HOSTING_TLS "Zertifikate: acme = Let's Encrypt, internal = eigene CA (Test), off = nur HTTP" "acme"
    case "$HOSTING_TLS" in acme|internal|off) ;; *) fail "Zertifikate: acme, internal oder off." ;; esac
    ask HOSTING_ACME_EMAIL "E-Mail-Adresse für Let's Encrypt (optional)" ""
    PROFILES=caddy
    PROXY_CONTAINER=estrichone-hosting-proxy
  else
    HOSTING_TLS=off
    ask HOSTING_PROXY_CONTAINER "Containername des vorhandenen Proxys (wird mit den Instanznetzen verbunden)" "nginx-proxy-manager"
    PROFILES=""
    PROXY_CONTAINER="$HOSTING_PROXY_CONTAINER"
  fi
  umask 077
  sed -e "s|^HOSTING_IMAGE=.*|HOSTING_IMAGE=$IMAGE|" \
      -e "s|^HOSTING_ROOT=.*|HOSTING_ROOT=$ROOT|" \
      -e "s|^HOSTING_BASE_DOMAIN=.*|HOSTING_BASE_DOMAIN=$HOSTING_BASE_DOMAIN|" \
      -e "s|^HOSTING_CONSOLE_DOMAIN=.*|HOSTING_CONSOLE_DOMAIN=$HOSTING_CONSOLE_DOMAIN|" \
      -e "s|^HOSTING_TLS=.*|HOSTING_TLS=$HOSTING_TLS|" \
      -e "s|^HOSTING_ACME_EMAIL=.*|HOSTING_ACME_EMAIL=${HOSTING_ACME_EMAIL:-}|" \
      -e "s|^HOSTING_PROXY=.*|HOSTING_PROXY=$HOSTING_PROXY|" \
      -e "s|^COMPOSE_PROFILES=.*|COMPOSE_PROFILES=$PROFILES|" \
      -e "s|^HOSTING_PROXY_CONTAINER=.*|HOSTING_PROXY_CONTAINER=$PROXY_CONTAINER|" \
      -e "s|^HOSTING_HTTP_PORT=.*|HOSTING_HTTP_PORT=${HOSTING_HTTP_PORT:-80}|" \
      -e "s|^HOSTING_HTTPS_PORT=.*|HOSTING_HTTPS_PORT=${HOSTING_HTTPS_PORT:-443}|" \
      -e "s|^ESTRICHONE_REGISTRY=.*|ESTRICHONE_REGISTRY=$REGISTRY|" \
      /opt/estrichone-hosting/env.example > "$ROOT/.env"
  [ -z "${HOSTING_DEFAULT_VERSION:-}" ] || set_env HOSTING_DEFAULT_VERSION "$HOSTING_DEFAULT_VERSION"
  umask 022
  FIRST=1
fi

mkdir -p "$ROOT/instances" "$ROOT/proxy" "$ROOT/proxy-data" "$ROOT/proxy-config" "$ROOT/archive" "$ROOT/console" "$ROOT/backups"
chmod 700 "$ROOT/console" "$ROOT/archive"

cd "$ROOT"
echo "Images laden und Konsole starten …"
docker compose -p estrichone-hosting pull --quiet --ignore-pull-failures
docker compose -p estrichone-hosting up -d --remove-orphans --wait --wait-timeout 180

# shellcheck source=/dev/null
set -a; . ./.env; set +a
echo
echo "Hosting-Konsole läuft. Erreichbar unter: https://$HOSTING_CONSOLE_DOMAIN"
echo "  (Notzugang auf dem Server: http://127.0.0.1:${HOSTING_CONSOLE_LOCAL_PORT:-8099})"
echo
echo "DNS: A/AAAA-Eintrag für $HOSTING_CONSOLE_DOMAIN und Wildcard *.$HOSTING_BASE_DOMAIN auf diesen Server."
if [ "${HOSTING_PROXY:-caddy}" = "extern" ]; then
  echo "Externer Proxy: Proxy-Host $HOSTING_CONSOLE_DOMAIN → http://estrichone-hosting-console:8080 (Container $HOSTING_PROXY_CONTAINER"
  echo "  mit dem Netz „estrichone-hosting“ verbinden: docker network connect estrichone-hosting $HOSTING_PROXY_CONTAINER)."
fi

CODE=""
for _ in $(seq 1 30); do
  CODE="$(docker logs estrichone-hosting-console 2>&1 | grep -o 'Einrichtungscode: [A-Z0-9-]*' | tail -n 1 | cut -d' ' -f2 || true)"
  [ -n "$CODE" ] && break
  docker logs estrichone-hosting-console 2>&1 | grep -q 'Now listening' && [ "$FIRST" = "0" ] && break
  sleep 2
done
if [ -n "$CODE" ]; then
  echo
  echo "Ersteinrichtung: Konsole öffnen und diesen Code eingeben (24 Stunden gültig, einmalig):"
  echo
  echo "   $CODE"
fi
