#!/bin/sh
# EstrichONE aktualisieren – vom Installer nach /opt/estrichone/update.sh gelegt.
#
#   sudo /opt/estrichone/update.sh            neueste veröffentlichte Version (auch Vorabversionen)
#   sudo /opt/estrichone/update.sh 0.1.0      bestimmte Version
#   sudo /opt/estrichone/update.sh --ohne-sicherung [VERSION]   ohne vorherige Sicherung (nicht empfohlen)
#
# Ablauf: Version ermitteln → Sicherung → Installer der Zielversion ausführen (Konfiguration und Daten bleiben)
# → warten, bis alle Dienste gesund sind → Version prüfen.
# shellcheck disable=SC1111,SC1112
set -eu

DIR="$(cd "$(dirname "$0")" && pwd)"
REPO="${ESTRICHONE_GITHUB_REPO:-florians25/EstrichONE}"
BACKUP=1
TARGET=""
for arg in "$@"; do
  case "$arg" in
    --ohne-sicherung|--no-backup) BACKUP=0 ;;
    -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
    v*|[0-9]*) TARGET="${arg#v}" ;;
    *) echo "Unbekannte Angabe: $arg (Hilfe: $0 --help)" >&2; exit 2 ;;
  esac
done

fail() { echo; echo "FEHLER: $*" >&2; exit 1; }
[ "$(id -u)" = "0" ] || fail "Bitte mit sudo ausführen: sudo $0 $*"
[ "$DIR" = "/opt/estrichone" ] || fail "update.sh muss in /opt/estrichone liegen (Installationsverzeichnis des Installers)."
[ -f "$DIR/.env" ] || fail "Keine Installation in $DIR gefunden (.env fehlt)."
command -v docker >/dev/null 2>&1 || fail "Docker ist nicht installiert."
env_value() { sed -n "s/^$1=//p" "$DIR/.env" | tail -n 1; }
REGISTRY="$(env_value ESTRICHONE_REGISTRY)"; REGISTRY="${REGISTRY:-ghcr.io/florians25}"
CURRENT="$(env_value ESTRICHONE_VERSION)"

fetch() {
  if command -v curl >/dev/null 2>&1; then curl -fsSL "$1"
  elif command -v wget >/dev/null 2>&1; then wget -qO- "$1"
  else docker run --rm --entrypoint wget "$REGISTRY/estrichone-installer:${CURRENT:-latest}" -qO- "$1"
  fi
}

if [ -z "$TARGET" ]; then
  echo "Neueste Version ermitteln …"
  TARGET="$(fetch "https://api.github.com/repos/$REPO/releases?per_page=10" 2>/dev/null \
    | grep -o '"tag_name": *"v[^"]*"' | head -n 1 | sed 's/.*"v\([^"]*\)"/\1/')" || true
  [ -n "$TARGET" ] || fail "Neueste Version nicht ermittelbar (Internet?). Version angeben: sudo $0 0.1.0"
fi

echo
echo "  Installiert:  ${CURRENT:-unbekannt}"
echo "  Neu:          $TARGET"
echo
if [ "$CURRENT" = "$TARGET" ]; then
  echo "EstrichONE ist bereits auf Version $TARGET. Erneut einspielen: sudo $0 $TARGET (läuft trotzdem weiter)."
fi

INSTALLER="$REGISTRY/estrichone-installer:$TARGET"
docker pull -q "$INSTALLER" >/dev/null 2>&1 || docker image inspect "$INSTALLER" >/dev/null 2>&1 \
  || fail "Version $TARGET gibt es nicht ($INSTALLER)."

cd "$DIR"
if [ "$BACKUP" = "1" ]; then
  if docker compose -p estrichone ps --status running --services 2>/dev/null | grep -qx backup; then
    echo "Sicherung vor dem Update …"
    docker compose -p estrichone exec -T backup estrichone-backup run \
      || fail "Sicherung fehlgeschlagen – Update abgebrochen. Ohne Sicherung: sudo $0 --ohne-sicherung $TARGET"
  else
    echo "Hinweis: Der Backup-Dienst läuft nicht – es wird keine Sicherung vor dem Update erstellt."
  fi
fi

echo "Update auf $TARGET …"
docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "$DIR:/opt/estrichone" "$INSTALLER" \
  || fail "Der Installer meldet einen Fehler (Ausgabe oben). Daten und Konfiguration sind unverändert erhalten."

echo "Warte, bis alle Dienste bereit sind …"
healthy=""
for _ in $(seq 1 90); do
  state="$(docker compose -p estrichone ps --format '{{.Service}} {{.State}} {{.Health}}' 2>/dev/null || true)"
  if echo "$state" | grep -q '^api running healthy' && echo "$state" | grep -q '^caddy running'; then healthy=1; break; fi
  sleep 2
done
[ -n "$healthy" ] || { docker compose -p estrichone ps; fail "Dienste nicht bereit. Protokoll: cd $DIR && sudo docker compose -p estrichone logs --tail 100 api"; }

RUNNING="$(docker run --rm --network estrichone_frontend --entrypoint wget "$INSTALLER" -qO- http://api:8080/api/v1/server-info 2>/dev/null \
  | grep -o '"serverVersion":"[^"]*"' | cut -d'"' -f4 || true)"
case "$RUNNING" in
  "$TARGET"*) ;;
  *) fail "Der Server meldet Version '${RUNNING:-?}' statt $TARGET." ;;
esac
echo
echo "======================================================================"
echo " Update erfolgreich: EstrichONE ${CURRENT:-?} → $TARGET (Server meldet ${RUNNING:-?})."
echo "======================================================================"
