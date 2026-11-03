#!/bin/sh
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112,SC1091
# EstrichONE Hosting-Konsole – Installer im Image. Bequemer Weg auf dem Server (ein Befehl, fragt alles ab):
#
#   curl -fsSL https://raw.githubusercontent.com/florians25/EstrichONE/main/hosting/install.sh | sudo bash
#
# Direkt mit Docker (gleiche Wirkung):
#   docker run --rm -it -v /var/run/docker.sock:/var/run/docker.sock -v /srv/estrichone-hosting:/srv/estrichone-hosting \
#     ghcr.io/florians25/estrichone-hosting:latest install
#
# Befehle: install (Standard; erneut ausführen = Reparatur bzw. Aktualisierung auf die Version dieses Images),
#          reset-admin [BENUTZER] (Notfall: neues Startpasswort, Zwei-Faktor aus), uninstall [--purge].
# Ohne Rückfragen: -e HOSTING_BASE_DOMAIN=estrichone-hosting.de -e HOSTING_ACME_EMAIL=admin@firma.de
# Weitere Optionen: -e HOSTING_MODE=direkt|proxy -e HOSTING_CONSOLE_DOMAIN=konsole.… -e HOSTING_TLS=acme|internal|off
#                   -e HOSTING_ADMIN_USER=admin -e HOSTING_ADMIN_EMAIL=… -e HOSTING_ADMIN_PASSWORD=… (sonst zufällig)
#                   -e HOSTING_HTTP_PORT=80 -e HOSTING_HTTPS_PORT=443 -e HOSTING_ROOT=/srv/estrichone-hosting
set -eu

COMMAND="${1:-install}"
[ $# -gt 0 ] && shift
ROOT="${HOSTING_ROOT:-/srv/estrichone-hosting}"
VERSION="${HOSTING_IMAGE_VERSION:-latest}"
REGISTRY="${ESTRICHONE_REGISTRY:-ghcr.io/florians25}"
IMAGE="${HOSTING_IMAGE:-$REGISTRY/estrichone-hosting:$VERSION}"
CONSOLE=estrichone-hosting-console

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
env_value() { grep -E "^$1=" "$ROOT/.env" 2>/dev/null | tail -n 1 | cut -d= -f2-; }
valid_domain() { echo "$1" | grep -Eq '^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9-]{2,63}$'; }
valid_user() { echo "$1" | grep -Eq '^[a-z][a-z0-9._-]{2,31}$'; }
valid_email() { [ -z "$1" ] || echo "$1" | grep -Eq '^[^@[:space:]"]+@[^@[:space:]"]+\.[^@[:space:]"]+$'; }
# Gut lesbares Startpasswort: 4×4 Zeichen ohne verwechselbare Zeichen (≈ 80 Bit).
new_password() { tr -dc 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789' < /dev/urandom | head -c 16 | sed 's/\(....\)/\1-/g; s/-$//'; }
console_healthy() { [ "$(docker inspect -f '{{.State.Health.Status}}' "$CONSOLE" 2>/dev/null || true)" = "healthy" ]; }

# Admin-Konto über die Konsole anlegen/zurücksetzen: Datei im (nur root lesbaren) Konsolenverzeichnis, die Konsole liest
# sie beim Start, legt das Konto an (Passwort bei der ersten Anmeldung ändern) und löscht sie sofort.
write_admin() { # write_admin BENUTZER PASSWORT EMAIL RESET
  umask 077
  printf '{"userName":"%s","password":"%s","email":"%s","reset":%s}\n' "$1" "$2" "$3" "$4" > "$ROOT/console/initial-admin.json"
  umask 022
}
wait_admin_applied() {
  for _ in $(seq 1 90); do
    [ ! -f "$ROOT/console/initial-admin.json" ] && console_healthy && return 0
    sleep 2
  done
  return 1
}

[ -S /var/run/docker.sock ] || fail "Docker-Socket fehlt: bitte mit -v /var/run/docker.sock:/var/run/docker.sock starten."
docker version >/dev/null 2>&1 || fail "Docker ist über den Socket nicht erreichbar."
if ! { [ -d "$ROOT" ] && touch "$ROOT/.schreibtest" 2>/dev/null; }; then
  fail "Bitte das Verzeichnis einbinden: -v $ROOT:$ROOT (gleicher Pfad innen und außen)."
fi
rm -f "$ROOT/.schreibtest"

case "$COMMAND" in
  install|update|repair) ;;
  reset-admin)
    [ -f "$ROOT/.env" ] || fail "Keine Installation unter $ROOT."
    USER_NAME="${1:-${HOSTING_ADMIN_USER:-admin}}"
    valid_user "$USER_NAME" || fail "Ungültiger Benutzername: $USER_NAME"
    PASSWORD="${HOSTING_ADMIN_PASSWORD:-$(new_password)}"
    [ "${#PASSWORD}" -ge 12 ] || fail "Das Passwort muss mindestens 12 Zeichen lang sein."
    write_admin "$USER_NAME" "$PASSWORD" "" true
    docker restart "$CONSOLE" >/dev/null
    wait_admin_applied || fail "Die Konsole hat das Konto nicht übernommen – Protokoll: docker logs $CONSOLE"
    echo "Administrator „$USER_NAME“ zurückgesetzt (alle Sitzungen beendet, Zwei-Faktor ausgeschaltet)."
    echo "  Startpasswort: $PASSWORD   – bei der nächsten Anmeldung wird ein neues Passwort verlangt."
    exit 0 ;;
  uninstall)
    [ -f "$ROOT/compose.yml" ] || fail "Keine Installation unter $ROOT."
    PURGE=0
    [ "${1:-}" = "--purge" ] && PURGE=1
    if [ "$PURGE" = "1" ]; then
      CONFIRM="${HOSTING_PURGE_CONFIRM:-}"
      if [ -z "$CONFIRM" ] && [ -t 0 ]; then
        echo "ACHTUNG: Entfernt ALLE Instanzen mit Datenbanken, Dateien und Sicherungen unter $ROOT – endgültig."
        printf 'Zum Bestätigen LÖSCHEN eingeben: '; read -r CONFIRM || true
      fi
      [ "$CONFIRM" = "LÖSCHEN" ] || fail "Nicht bestätigt – nichts entfernt."
      for dir in "$ROOT"/instances/*/; do
        [ -f "$dir/.env" ] || continue
        slug="$(basename "$dir")"
        echo "Instanz $slug entfernen …"
        (cd "$dir" && docker compose -p "estrichone-$slug" -f compose.yml -f compose.override.yml -f compose.proxy.yml -f compose.hosting.yml \
          down --volumes --remove-orphans --timeout 30 >/dev/null 2>&1) || true
        docker network rm "estrichone-$slug-edge" >/dev/null 2>&1 || true
      done
    fi
    cd "$ROOT"
    docker compose -p estrichone-hosting --profile caddy down --remove-orphans
    if [ "$PURGE" = "1" ]; then
      find "$ROOT" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
      echo "Hosting-Konsole und alle Instanzen entfernt; $ROOT ist leer."
    else
      echo "Hosting-Konsole und Proxy angehalten und entfernt. Instanzen und Daten unter $ROOT bleiben erhalten"
      echo "(laufende Instanzen sind ohne Proxy nicht erreichbar). Erneut installieren stellt alles wieder her."
    fi
    exit 0 ;;
  *) fail "Unbekannter Befehl „$COMMAND“ (install, reset-admin, uninstall)." ;;
esac

echo "EstrichONE Hosting-Konsole – Installation nach $ROOT ($IMAGE)"
cp /opt/estrichone-hosting/compose.yml "$ROOT/compose.yml"
ADMIN_CREATED=0

if [ -f "$ROOT/.env" ]; then
  echo "Vorhandene Installation gefunden – Konfiguration bleibt, Konsole wird auf $IMAGE gesetzt (Reparatur/Aktualisierung)."
  set_env HOSTING_IMAGE "$IMAGE"
  FIRST=0
else
  # Sinnvolle Vorschläge: Domain des Servers, Betriebsart „direkt“ (eigenes HTTPS), Konto „admin“.
  SUGGESTED="$(echo "${HOSTING_SERVER_FQDN:-}" | tr '[:upper:]' '[:lower:]')"
  valid_domain "$SUGGESTED" || SUGGESTED="estrichone-hosting.de"
  ask HOSTING_BASE_DOMAIN "Basis-Domain für Instanzen (DNS: *.<domain> → dieser Server)" "$SUGGESTED"
  HOSTING_BASE_DOMAIN="$(echo "$HOSTING_BASE_DOMAIN" | tr '[:upper:]' '[:lower:]')"
  valid_domain "$HOSTING_BASE_DOMAIN" || fail "Ungültige Domain: $HOSTING_BASE_DOMAIN"
  ask HOSTING_CONSOLE_DOMAIN "Adresse der Konsole" "konsole.$HOSTING_BASE_DOMAIN"
  HOSTING_CONSOLE_DOMAIN="$(echo "$HOSTING_CONSOLE_DOMAIN" | tr '[:upper:]' '[:lower:]')"
  valid_domain "$HOSTING_CONSOLE_DOMAIN" || fail "Ungültige Domain: $HOSTING_CONSOLE_DOMAIN"
  # Betriebsart wie beim Suite-Installer; HOSTING_PROXY=caddy|extern (ältere Aufrufe) gilt weiter.
  case "${HOSTING_PROXY:-}" in caddy) HOSTING_MODE="${HOSTING_MODE:-direkt}" ;; extern) HOSTING_MODE="${HOSTING_MODE:-proxy}" ;; esac
  ask HOSTING_MODE "Betriebsart: direkt = eigenes HTTPS auf Port 80/443 (Let's Encrypt), proxy = hinter vorhandenem Proxy (z. B. Nginx Proxy Manager)" "direkt"
  case "$HOSTING_MODE" in direkt) HOSTING_PROXY=caddy ;; proxy) HOSTING_PROXY=extern ;; *) fail "Betriebsart muss direkt oder proxy sein." ;; esac
  if [ "$HOSTING_PROXY" = "caddy" ]; then
    ask HOSTING_TLS "Zertifikate: acme = Let's Encrypt, internal = eigene CA (Test), off = nur HTTP" "acme"
    case "$HOSTING_TLS" in acme|internal|off) ;; *) fail "Zertifikate: acme, internal oder off." ;; esac
    ask HOSTING_ACME_EMAIL "E-Mail-Adresse für Let's Encrypt und Benachrichtigungen (empfohlen)" ""
    PROFILES=caddy
    PROXY_CONTAINER=estrichone-hosting-proxy
  else
    HOSTING_TLS=off
    ask HOSTING_PROXY_CONTAINER "Containername des vorhandenen Proxys (wird mit den Instanznetzen verbunden)" "nginx-proxy-manager"
    PROFILES=""
    PROXY_CONTAINER="$HOSTING_PROXY_CONTAINER"
  fi
  valid_email "${HOSTING_ACME_EMAIL:-}" || fail "Ungültige E-Mail-Adresse: $HOSTING_ACME_EMAIL"
  ask HOSTING_ADMIN_USER "Benutzername des ersten Administrators" "admin"
  valid_user "$HOSTING_ADMIN_USER" || fail "Benutzername: 3–32 Zeichen a–z, 0–9, Punkt, Bindestrich, Unterstrich; beginnt mit Buchstabe."
  HOSTING_ADMIN_EMAIL="${HOSTING_ADMIN_EMAIL:-${HOSTING_ACME_EMAIL:-}}"
  valid_email "$HOSTING_ADMIN_EMAIL" || fail "Ungültige E-Mail-Adresse: $HOSTING_ADMIN_EMAIL"
  ADMIN_PASSWORD="${HOSTING_ADMIN_PASSWORD:-$(new_password)}"
  [ "${#ADMIN_PASSWORD}" -ge 12 ] || fail "HOSTING_ADMIN_PASSWORD muss mindestens 12 Zeichen lang sein."
  # shellcheck disable=SC1003
  case "$ADMIN_PASSWORD" in *'"'*|*'\'*) fail "HOSTING_ADMIN_PASSWORD darf keine Anführungszeichen oder Backslashes enthalten." ;; esac
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
  [ -z "${HOSTING_INSECURE_COOKIES:-}" ] || set_env HOSTING_INSECURE_COOKIES "$HOSTING_INSECURE_COOKIES"
  umask 022
  FIRST=1
fi

# .env enthält Geheimnisse: nur root (auch bei älteren Installationen nachziehen).
chmod 600 "$ROOT/.env"
# Optionale IP-Allowlist der Konsole: -e HOSTING_ALLOWED_NETWORKS="203.0.113.7, 10.0.0.0/8" (leer = aus)
[ -z "${HOSTING_ALLOWED_NETWORKS+x}" ] || set_env HOSTING_ALLOWED_NETWORKS "$(printf %s "$HOSTING_ALLOWED_NETWORKS" | tr -d " ")"
mkdir -p "$ROOT/instances" "$ROOT/proxy" "$ROOT/proxy-data" "$ROOT/proxy-config" "$ROOT/archive" "$ROOT/console" "$ROOT/backups"
chmod 700 "$ROOT/console" "$ROOT/archive"
if [ "$FIRST" = "1" ]; then
  write_admin "$HOSTING_ADMIN_USER" "$ADMIN_PASSWORD" "$HOSTING_ADMIN_EMAIL" false
  ADMIN_CREATED=1
fi

cd "$ROOT"
echo "Images laden und Konsole starten …"
docker compose -p estrichone-hosting pull --quiet --ignore-pull-failures
docker compose -p estrichone-hosting up -d --remove-orphans --wait --wait-timeout 180

# shellcheck source=/dev/null
set -a; . ./.env; set +a
if [ "$ADMIN_CREATED" = "1" ] && ! wait_admin_applied; then
  echo "WARNUNG: Die Konsole hat das Admin-Konto noch nicht übernommen – Protokoll: docker logs $CONSOLE" >&2
  ADMIN_CREATED=0
fi

SCHEME=https
[ "${HOSTING_TLS:-acme}" = "off" ] && [ "${HOSTING_PROXY:-caddy}" = "caddy" ] && SCHEME=http
LOCAL="http://127.0.0.1:${HOSTING_CONSOLE_LOCAL_PORT:-8099}"
line() { echo "  ───────────────────────────────────────────────────────────────────────────────"; }
echo
line
echo "  EstrichONE Hosting-Konsole läuft ($(docker inspect -f '{{.Config.Image}}' "$CONSOLE" 2>/dev/null || echo "$IMAGE"))"
line
echo "  Adresse:      $SCHEME://$HOSTING_CONSOLE_DOMAIN"
echo "  Notzugang:    $LOCAL  (nur auf dem Server bzw. per SSH-Tunnel: ssh -L 8099:127.0.0.1:${HOSTING_CONSOLE_LOCAL_PORT:-8099} <server>)"
if [ "$ADMIN_CREATED" = "1" ]; then
  echo "  Benutzer:     $HOSTING_ADMIN_USER"
  echo "  Startpasswort: $ADMIN_PASSWORD"
  echo "                (bei der ersten Anmeldung wird ein eigenes Passwort verlangt; danach führt ein Assistent durch"
  echo "                 Zwei-Faktor, Standardversion, zweites Sicherungsziel und Benachrichtigungen)"
else
  echo "  Anmeldung:    mit dem bestehenden Administrator (vergessen? sudo estrichone-hosting reset-admin)"
fi
echo "  Daten:        $ROOT  (.env nur für root lesbar – in Server-Backups verschlüsselt sichern)"
line
echo "  Nächste Schritte:"
echo "   1. DNS: A/AAAA für $HOSTING_CONSOLE_DOMAIN und Wildcard *.$HOSTING_BASE_DOMAIN auf diesen Server."
if [ "${HOSTING_PROXY:-caddy}" = "extern" ]; then
  echo "   2. Proxy: Proxy-Host $HOSTING_CONSOLE_DOMAIN → http://$CONSOLE:8080 anlegen und den Proxy-Container"
  echo "      verbinden: docker network connect estrichone-hosting $HOSTING_PROXY_CONTAINER"
else
  echo "   2. Ports 80 und 443 (TCP, 443 auch UDP) in der Firewall freigeben – Zertifikate holt Caddy automatisch."
fi
echo "   3. Konsole öffnen, anmelden, Einrichtungsassistent abschließen, erste Instanz anlegen."
echo "  Verwaltung: sudo estrichone-hosting status | update | logs | reset-admin | uninstall"
line

# Fallback: Konsole ohne Administrator (z. B. Konto aus dem Installer abgelehnt) zeigt einen Einrichtungscode.
CODE="$(docker logs "$CONSOLE" 2>&1 | grep -o 'Einrichtungscode: [A-Z0-9-]*' | tail -n 1 | cut -d' ' -f2 || true)"
if [ -n "$CODE" ] && [ "$ADMIN_CREATED" = "0" ] && [ "$FIRST" = "1" ]; then
  echo
  echo "Ersteinrichtung: Konsole öffnen und diesen Code eingeben (24 Stunden gültig, einmalig):"
  echo
  echo "   $CODE"
fi
