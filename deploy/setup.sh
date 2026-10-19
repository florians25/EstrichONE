#!/usr/bin/env bash
# EstrichONE – Ersteinrichtung des Servers.
#   ./setup.sh                                   fragt Domain und Firmennamen ab
#   ./setup.sh --domain estrichone.firma.de --name "Estrichbau Muster GmbH" --yes
# Legt .env mit zufälligem Datenbankpasswort an und bereitet die Datenverzeichnisse vor.
# Eine vorhandene .env wird nie überschrieben.
set -euo pipefail
cd "$(dirname "$0")"

DOMAIN=""
NAME=""
ASSUME_YES=0
API_UID=1654   # Benutzer „app“ im .NET-Container

while [[ $# -gt 0 ]]; do
  case "$1" in
    --domain) DOMAIN="$2"; shift 2 ;;
    --name) NAME="$2"; shift 2 ;;
    --yes|-y) ASSUME_YES=1; shift ;;
    -h|--help) sed -n '2,6p' "$0"; exit 0 ;;
    *) echo "Unbekannte Option: $1" >&2; exit 2 ;;
  esac
done

fail() { echo "FEHLER: $*" >&2; exit 1; }

command -v docker >/dev/null || fail "Docker ist nicht installiert (https://docs.docker.com/engine/install/)."
docker compose version >/dev/null 2>&1 || fail "Das Docker-Compose-Plugin fehlt."

if [[ -f .env ]]; then
  echo "Hinweis: .env existiert bereits und bleibt unverändert."
else
  if [[ -z "$DOMAIN" && $ASSUME_YES -eq 0 ]]; then
    read -rp "Adresse des Servers (z. B. estrichone.meinefirma.de) [localhost]: " DOMAIN
  fi
  if [[ -z "$NAME" && $ASSUME_YES -eq 0 ]]; then
    read -rp "Firmenname für die Anzeige in den Apps [EstrichONE]: " NAME
  fi
  DOMAIN="${DOMAIN:-localhost}"
  NAME="${NAME:-EstrichONE}"
  [[ "$DOMAIN" =~ ^[A-Za-z0-9.-]+$ ]] || fail "Ungültige Adresse: $DOMAIN (ohne https:// und ohne Pfad angeben)."
  [[ "$NAME" != *'"'* ]] || fail "Der Firmenname darf keine Anführungszeichen enthalten."

  PASSWORD="$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 40)"
  umask 077
  sed -e "s|^ESTRICHONE_DOMAIN=.*|ESTRICHONE_DOMAIN=${DOMAIN}|" \
      -e "s|^ESTRICHONE_SERVER_NAME=.*|ESTRICHONE_SERVER_NAME=\"${NAME}\"|" \
      -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=${PASSWORD}|" \
      .env.example > .env
  echo ".env angelegt (nur für root lesbar). Bitte zusätzlich sicher verwahren – sie ist nicht Teil des Backups."
fi

set -a
# shellcheck source=/dev/null
source .env
set +a
DATA_DIR="${ESTRICHONE_DATA_DIR:-./data}"
mkdir -p "$DATA_DIR/postgres" "$DATA_DIR/files" "$DATA_DIR/caddy/data" "$DATA_DIR/caddy/config"
if [[ $(id -u) -eq 0 ]]; then
  chown -R "$API_UID:$API_UID" "$DATA_DIR/files"
else
  echo "Hinweis: Ohne root-Rechte konnte $DATA_DIR/files nicht dem API-Benutzer (UID $API_UID) übergeben werden:"
  echo "         sudo chown -R $API_UID:$API_UID $DATA_DIR/files"
fi

echo
echo "Fertig. Starten mit:  docker compose up -d --build"
echo "Prüfen mit:           curl https://${ESTRICHONE_DOMAIN}/api/v1/server-info"
