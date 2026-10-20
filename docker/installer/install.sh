#!/bin/sh
# EstrichONE – Installation und Aktualisierung mit einem Docker-Befehl:
#
#   docker run --rm -it -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone:/opt/estrichone \
#     ghcr.io/florians25/estrichone-installer:latest
#
# Ohne Rückfragen:  -e ESTRICHONE_DOMAIN=estrichone.firma.de -e ESTRICHONE_NAME="Estrichbau Muster GmbH"
# Ports (Standard 8181/8143): -e ESTRICHONE_HTTP_PORT=8181 -e ESTRICHONE_HTTPS_PORT=8143
# Erneut ausführen = auf die Version dieses Installers aktualisieren; Daten und .env bleiben erhalten.
set -eu

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

[ -S /var/run/docker.sock ] || fail "Docker-Socket fehlt: bitte mit -v /var/run/docker.sock:/var/run/docker.sock starten."
docker version >/dev/null 2>&1 || fail "Docker ist über den Socket nicht erreichbar."
[ -d "$DIR" ] && touch "$DIR/.schreibtest" 2>/dev/null && rm -f "$DIR/.schreibtest" \
  || fail "Bitte das Installationsverzeichnis einbinden: -v /opt/estrichone:/opt/estrichone (gleicher Pfad innen und außen)."

echo "EstrichONE $VERSION – Installation nach $DIR"
cp /installer/compose.yml /installer/compose.override.yml "$DIR/"

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
    domain="$(sed -n 's/^ESTRICHONE_DOMAIN=//p' "$DIR/.env")"
    url="https://$domain"; [ "$ESTRICHONE_HTTPS_PORT" = "443" ] || url="$url:$ESTRICHONE_HTTPS_PORT"
    sed -i "s|^ESTRICHONE_PUBLIC_URL=.*|ESTRICHONE_PUBLIC_URL=$url|" "$DIR/.env"
  fi
  FIRST=0
else
  ask ESTRICHONE_DOMAIN "Adresse des Servers (Domain oder IP, ohne https://)" "localhost"
  ask ESTRICHONE_NAME "Firmenname für die Anzeige in den Apps" "EstrichONE"
  HTTP_PORT="${ESTRICHONE_HTTP_PORT:-8181}"
  HTTPS_PORT="${ESTRICHONE_HTTPS_PORT:-8143}"
  echo "$ESTRICHONE_DOMAIN" | grep -Eq '^[A-Za-z0-9.-]+$' || fail "Ungültige Adresse: $ESTRICHONE_DOMAIN"
  case "$ESTRICHONE_NAME" in *'"'*) fail "Der Firmenname darf keine Anführungszeichen enthalten." ;; esac
  PUBLIC_URL="https://$ESTRICHONE_DOMAIN"
  [ "$HTTPS_PORT" = "443" ] || PUBLIC_URL="$PUBLIC_URL:$HTTPS_PORT"
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
  printf 'ESTRICHONE_PUBLIC_URL=%s\nESTRICHONE_VERSION=%s\n' "$PUBLIC_URL" "$VERSION" >> "$DIR/.env"
  umask 022
  FIRST=1
fi

cd "$DIR"
set -a; . ./.env; set +a
DATA="${ESTRICHONE_DATA_DIR:-./data}"
mkdir -p "$DATA/postgres" "$DATA/caddy/data" "$DATA/caddy/config" "$DATA/files" "$DATA/backup-status" \
  "$DATA/backup-staging" "$DATA/restic-cache" "${ESTRICHONE_BACKUP_DIR:-./backup-repository}" "${ESTRICHONE_SSH_DIR:-./ssh}"
chown -R "$API_UID:$API_UID" "$DATA/files" "$DATA/backup-status" "$DATA/backup-staging" "$DATA/restic-cache" \
  "${ESTRICHONE_BACKUP_DIR:-./backup-repository}"

echo "Images laden und Dienste starten …"
docker compose -p estrichone pull --quiet --ignore-pull-failures
docker compose -p estrichone up -d --remove-orphans

URL="https://$ESTRICHONE_DOMAIN"
[ "$ESTRICHONE_HTTPS_PORT" = "443" ] || URL="$URL:$ESTRICHONE_HTTPS_PORT"
echo
echo "EstrichONE läuft: $URL   (HTTP-Port $ESTRICHONE_HTTP_PORT leitet weiter)"

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
case "$ESTRICHONE_DOMAIN" in
  localhost|*[0-9].[0-9]*) echo; echo "Hinweis: Für $ESTRICHONE_DOMAIN stellt Caddy ein eigenes Zertifikat aus – der Browser warnt einmal." ;;
esac
