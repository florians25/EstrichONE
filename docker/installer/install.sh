#!/bin/sh
# EstrichONE – Installation und Aktualisierung mit einem Docker-Befehl:
#
#   docker run --rm -it -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone:/opt/estrichone \
#     ghcr.io/florians25/estrichone-installer:latest
#
# Ohne Rückfragen:  -e ESTRICHONE_DOMAIN=estrichone.firma.de -e ESTRICHONE_NAME="Estrichbau Muster GmbH"
# Ports (Standard 8181/8143): -e ESTRICHONE_HTTP_PORT=8181 -e ESTRICHONE_HTTPS_PORT=8143
# Betriebsart: -e ESTRICHONE_MODE=proxy  – hinter Nginx Proxy Manager o. Ä.: nur HTTP auf 8181, HTTPS macht der Proxy
#              -e ESTRICHONE_MODE=direkt – Caddy macht HTTPS selbst (8143) und leitet 8181 dorthin weiter
# Erneut ausführen = auf die Version dieses Installers aktualisieren; Daten und .env bleiben erhalten.
set -eu

# Weitere Rechner: Archivserver bzw. Backup-Ziel (siehe peer.sh)
case "${1:-}" in
  archiv|archive|backup-ziel|backup-target) exec estrichone-peer "$@" ;;
esac

DIR=/opt/estrichone
VERSION="${INSTALLER_VERSION:?}"
API_UID=1654

fail() { echo "FEHLER: $*" >&2; exit 1; }
ask() { # ask VAR "Frage" "Standard"
  eval "current=\${$1:-}"
  if [ -z "$current" ] && [ -t 0 ]; then
    printf '%s [%s]: ' "$2" "$3"; read -r current || true
  fi
  eval "$1=\"\${current:-$3}\""
}
set_env() { # set_env SCHLÜSSEL WERT – ersetzt oder ergänzt eine Zeile in der .env
  if grep -q "^$1=" "$DIR/.env"; then sed -i "s|^$1=.*|$1=$2|" "$DIR/.env"; else echo "$1=$2" >> "$DIR/.env"; fi
}
ask_mode() { # ask_mode Standard
  ask ESTRICHONE_MODE "Betriebsart: proxy = hinter Nginx Proxy Manager o. Ä. (nur HTTP), direkt = eigenes HTTPS" "$1"
  case "$ESTRICHONE_MODE" in proxy|direkt) ;; *) fail "Betriebsart muss proxy oder direkt sein." ;; esac
}
apply_mode() { # schreibt Betriebsart, Compose-Dateien und öffentliche Adresse in die .env
  domain="$(sed -n 's/^ESTRICHONE_DOMAIN=//p' "$DIR/.env")"
  https_port="$(sed -n 's/^ESTRICHONE_HTTPS_PORT=//p' "$DIR/.env")"
  set_env ESTRICHONE_MODE "$ESTRICHONE_MODE"
  if [ "$ESTRICHONE_MODE" = "proxy" ]; then
    set_env COMPOSE_FILE compose.yml:compose.override.yml:compose.proxy.yml
    set_env ESTRICHONE_PUBLIC_URL "https://$domain"
  else
    set_env COMPOSE_FILE compose.yml:compose.override.yml
    url="https://$domain"; [ "$https_port" = "443" ] || url="$url:$https_port"
    set_env ESTRICHONE_PUBLIC_URL "$url"
  fi
}

[ -S /var/run/docker.sock ] || fail "Docker-Socket fehlt: bitte mit -v /var/run/docker.sock:/var/run/docker.sock starten."
docker version >/dev/null 2>&1 || fail "Docker ist über den Socket nicht erreichbar."
[ -d "$DIR" ] && touch "$DIR/.schreibtest" 2>/dev/null && rm -f "$DIR/.schreibtest" \
  || fail "Bitte das Installationsverzeichnis einbinden: -v /opt/estrichone:/opt/estrichone (gleicher Pfad innen und außen)."

echo "EstrichONE $VERSION – Installation nach $DIR"
cp /installer/compose.yml /installer/compose.override.yml /installer/compose.proxy.yml "$DIR/"
# Einfaches Aktualisieren: sudo /opt/estrichone/update.sh [VERSION]
cp /installer/update.sh "$DIR/update.sh" && chmod 755 "$DIR/update.sh"

if [ -f "$DIR/.env" ]; then
  echo "Vorhandene Installation gefunden – Konfiguration bleibt, Version wird auf $VERSION gesetzt."
  if grep -q '^ESTRICHONE_VERSION=' "$DIR/.env"; then
    sed -i "s|^ESTRICHONE_VERSION=.*|ESTRICHONE_VERSION=$VERSION|" "$DIR/.env"
  else
    echo "ESTRICHONE_VERSION=$VERSION" >> "$DIR/.env"
  fi
  # Ports beim Aktualisieren ändern: -e ESTRICHONE_HTTP_PORT=… / -e ESTRICHONE_HTTPS_PORT=…
  # 8180 war bis 0.1.0-preview.2 der Standard und kollidiert oft mit anderen Diensten → automatisch 8181.
  if [ -z "${ESTRICHONE_HTTP_PORT:-}" ] && grep -q '^ESTRICHONE_HTTP_PORT=8180$' "$DIR/.env"; then
    ESTRICHONE_HTTP_PORT=8181
    echo "HTTP-Port wird von 8180 auf 8181 umgestellt."
  fi
  if [ -n "${ESTRICHONE_HTTP_PORT:-}" ]; then
    sed -i "s|^ESTRICHONE_HTTP_PORT=.*|ESTRICHONE_HTTP_PORT=$ESTRICHONE_HTTP_PORT|" "$DIR/.env"
  fi
  if [ -n "${ESTRICHONE_HTTPS_PORT:-}" ]; then
    sed -i "s|^ESTRICHONE_HTTPS_PORT=.*|ESTRICHONE_HTTPS_PORT=$ESTRICHONE_HTTPS_PORT|" "$DIR/.env"
  fi
  if [ -n "${ESTRICHONE_DOMAIN:-}" ]; then
    echo "$ESTRICHONE_DOMAIN" | grep -Eq '^[A-Za-z0-9.-]+$' || fail "Ungültige Adresse: $ESTRICHONE_DOMAIN"
    set_env ESTRICHONE_DOMAIN "$ESTRICHONE_DOMAIN"
  fi
  # Betriebsart: bis 0.1.0-preview.3 gab es nur „direkt“; ältere Installationen werden einmal gefragt.
  [ -n "${ESTRICHONE_MODE:-}" ] || ESTRICHONE_MODE="$(sed -n 's/^ESTRICHONE_MODE=//p' "$DIR/.env")"
  ask_mode direkt
  apply_mode
  FIRST=0
else
  ask_mode proxy
  if [ "$ESTRICHONE_MODE" = "proxy" ]; then
    ask ESTRICHONE_DOMAIN "Domain, unter der der Proxy EstrichONE anbietet (ohne https://, z. B. estrichone.firma.de)" "localhost"
  else
    ask ESTRICHONE_DOMAIN "Adresse des Servers (Domain oder IP, ohne https://)" "localhost"
  fi
  ask ESTRICHONE_NAME "Firmenname für die Anzeige in den Apps" "EstrichONE"
  HTTP_PORT="${ESTRICHONE_HTTP_PORT:-8181}"
  HTTPS_PORT="${ESTRICHONE_HTTPS_PORT:-8143}"
  echo "$ESTRICHONE_DOMAIN" | grep -Eq '^[A-Za-z0-9.-]+$' || fail "Ungültige Adresse: $ESTRICHONE_DOMAIN"
  case "$ESTRICHONE_NAME" in *'"'*) fail "Der Firmenname darf keine Anführungszeichen enthalten." ;; esac
  PASSWORD="$(head -c 48 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 40)"
  BACKUP_PASSWORD="$(head -c 64 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 48)"
  umask 077
  sed -e "s|^ESTRICHONE_DOMAIN=.*|ESTRICHONE_DOMAIN=$ESTRICHONE_DOMAIN|" \
      -e "s|^ESTRICHONE_SERVER_NAME=.*|ESTRICHONE_SERVER_NAME=\"$ESTRICHONE_NAME\"|" \
      -e "s|^ESTRICHONE_HTTP_PORT=.*|ESTRICHONE_HTTP_PORT=$HTTP_PORT|" \
      -e "s|^ESTRICHONE_HTTPS_PORT=.*|ESTRICHONE_HTTPS_PORT=$HTTPS_PORT|" \
      -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$PASSWORD|" \
      -e "s|^RESTIC_PASSWORD=.*|RESTIC_PASSWORD=$BACKUP_PASSWORD|" \
      /installer/env.example > "$DIR/.env"
  echo "ESTRICHONE_VERSION=$VERSION" >> "$DIR/.env"
  apply_mode
  umask 022
  FIRST=1
fi

# Archivserver im selben Stack (nur ohne zweiten Rechner): -e ESTRICHONE_ARCHIVE=lokal; abschalten mit =aus
case "${ESTRICHONE_ARCHIVE:-}" in
  lokal|local) set_env COMPOSE_PROFILES archive ;;
  aus|off) sed -i '/^COMPOSE_PROFILES=/d' "$DIR/.env" ;;
esac

cd "$DIR"
set -a; . ./.env; set +a
DATA="${ESTRICHONE_DATA_DIR:-./data}"
mkdir -p "$DATA/postgres" "$DATA/caddy/data" "$DATA/caddy/config" "$DATA/files" "$DATA/backup-status" \
  "$DATA/backup-staging" "$DATA/restic-cache" "${ESTRICHONE_BACKUP_DIR:-./backup-repository}" "${ESTRICHONE_SSH_DIR:-./ssh}" \
  "${ESTRICHONE_ARCHIVE_DIR:-./archive}"
chown -R "$API_UID:$API_UID" "$DATA/files" "$DATA/backup-status" "$DATA/backup-staging" "$DATA/restic-cache" \
  "${ESTRICHONE_BACKUP_DIR:-./backup-repository}" "${ESTRICHONE_ARCHIVE_DIR:-./archive}"

echo "Images laden und Dienste starten …"
docker compose -p estrichone pull --quiet --ignore-pull-failures

# Datenbank zuerst starten und das Passwort aus der .env setzen. Hilft, wenn die .env neu erzeugt wurde, das
# Datenverzeichnis aber noch mit einem älteren Passwort angelegt ist (sonst: „password authentication failed“).
# Im Container ist der lokale Zugang ohne Passwort erlaubt (Standard des postgres-Images).
docker compose -p estrichone up -d --remove-orphans --wait postgres
DB_USER="${POSTGRES_USER:-estrichone}"
case "$POSTGRES_PASSWORD$DB_USER" in *"'"*|*'"'*) fail "Datenbank-Benutzer/-Passwort enthalten Anführungszeichen." ;; esac
docker compose -p estrichone exec -T postgres psql -q -v ON_ERROR_STOP=1 -U "$DB_USER" -d "${POSTGRES_DB:-estrichone}" \
  -c "ALTER USER \"$DB_USER\" PASSWORD '$POSTGRES_PASSWORD';" >/dev/null \
  || fail "Datenbank-Passwort konnte nicht abgeglichen werden (siehe: docker compose -p estrichone logs postgres)."

docker compose -p estrichone up -d --remove-orphans

URL="$ESTRICHONE_PUBLIC_URL"
echo
if [ "$ESTRICHONE_MODE" = "proxy" ]; then
  echo "EstrichONE läuft auf diesem Server unter http://<server-ip>:$ESTRICHONE_HTTP_PORT (nur HTTP, für den Proxy)."
  echo
  echo "Im Nginx Proxy Manager einen Proxy Host anlegen:"
  echo "  Domain: $ESTRICHONE_DOMAIN   →   Scheme http, Host <server-ip>, Port $ESTRICHONE_HTTP_PORT"
  echo "  SSL: Let's-Encrypt-Zertifikat, Force SSL einschalten (die Anmeldung im Web braucht HTTPS)"
  echo "  Für große Videos unter Advanced eintragen:  client_max_body_size 600m;"
  echo
  echo "Danach erreichbar unter: $URL"
else
  echo "EstrichONE läuft: $URL   (HTTP-Port $ESTRICHONE_HTTP_PORT leitet weiter)"
fi

if [ "$FIRST" = "1" ]; then
  echo
  echo "======================================================================"
  echo " BACKUP-PASSWORT – jetzt notieren und AUSSERHALB des Servers aufbewahren:"
  echo
  echo "   $RESTIC_PASSWORD"
  echo
  echo " Ohne dieses Passwort lässt sich keine Sicherung wiederherstellen."
  echo "======================================================================"
fi

# Einrichtungscode für den ersten Administrator – nur solange der Server noch nicht eingerichtet ist.
# Abfrage im internen Netz mit diesem Image (wget aus BusyBox), damit kein weiteres Image nötig ist.
SELF="${ESTRICHONE_REGISTRY:-ghcr.io/florians25}/estrichone-installer:$VERSION"
setup_required() {
  docker run --rm --network estrichone_frontend --entrypoint wget "$SELF" -qO- http://api:8080/api/v1/server-info 2>/dev/null \
    | grep -q '"setupRequired":true'
}
CODE=""
if setup_required; then
  for _ in $(seq 1 60); do
    CODE="$(docker compose -p estrichone logs api 2>/dev/null | grep -o 'Einrichtungscode: [A-Z0-9-]*' | tail -n 1 | cut -d' ' -f2 || true)"
    [ -n "$CODE" ] && break
    sleep 2
  done
fi
if [ -n "$CODE" ]; then
  echo
  echo "Ersteinrichtung: $URL öffnen, „Jetzt einrichten“ wählen und diesen Code eingeben:"
  echo
  echo "   $CODE"
fi
if [ "${COMPOSE_PROFILES:-}" = "archive" ]; then
  ACODE="$(docker compose -p estrichone logs archive 2>/dev/null | sed -n 's/.*KOPPLUNGSCODE: *\([A-Z0-9-]*\).*/\1/p' | tail -n 1)"
  echo
  echo "Archivserver im Stack aktiv. Koppeln unter System → Archiv mit Adresse „archive“${ACODE:+ und Code $ACODE}."
fi
echo
echo "Aktualisieren später mit:  sudo /opt/estrichone/update.sh   (oder: sudo /opt/estrichone/update.sh <version>)"
[ "$ESTRICHONE_MODE" = "proxy" ] || case "$ESTRICHONE_DOMAIN" in
  localhost|*[0-9].[0-9]*) echo; echo "Hinweis: Für $ESTRICHONE_DOMAIN stellt Caddy ein eigenes Zertifikat aus – der Browser warnt einmal." ;;
esac
