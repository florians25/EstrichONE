#!/bin/sh
# EstrichONE-Archivserver bzw. Backup-Ziel auf einem weiteren Rechner einrichten oder aktualisieren – ein Befehl:
#
#   sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest archiv
#   sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest backup-ziel
#
# Optionen (-e …): ESTRICHONE_PORT=8443 (Backup-Ziel: 8444), ESTRICHONE_NAME="Archiv Keller", ESTRICHONE_ADDRESS=192.168.1.50 (sonst automatisch),
#   ESTRICHONE_DATA_DIR=/srv/archiv (Ordner auf dem Rechner statt Docker-Volume), ESTRICHONE_MIN_RETENTION_YEARS=10 (nur Archiv),
#   ESTRICHONE_NEW_CODE=1 (neuen Kopplungscode ausgeben, z. B. für eine weitere EstrichONE-Installation).
# Erneut ausführen = aktualisieren (Daten, Zertifikat und Kopplungen bleiben).
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112
set -eu

VERSION="${INSTALLER_VERSION:?}"
REGISTRY="${ESTRICHONE_REGISTRY:-ghcr.io/florians25}"
fail() { echo "FEHLER: $*" >&2; exit 1; }

case "${1:-}" in
  archiv|archive) ROLE=archive; TITLE="Archivserver"; WHERE="System → Archiv → „Archivserver koppeln“" ;;
  backup-ziel|backup-target) ROLE=backup-target; TITLE="Backup-Ziel"; WHERE="System → Backup-Ziele → „Backup-Ziel koppeln“" ;;
  *) fail "Unbekannte Rolle '${1:-}' (archiv oder backup-ziel)." ;;
esac

[ -S /var/run/docker.sock ] || fail "Docker-Socket fehlt: bitte mit -v /var/run/docker.sock:/var/run/docker.sock starten."
docker version >/dev/null 2>&1 || fail "Docker ist über den Socket nicht erreichbar."

NAME="estrichone-$ROLE"
IMAGE="$REGISTRY/estrichone-$ROLE:$VERSION"
# Standardports: Archivserver 8443, Backup-Ziel 8444 (beide auf einem Rechner möglich)
DEFAULT_PORT=8443; [ "$ROLE" = "backup-target" ] && DEFAULT_PORT=8444
PORT="${ESTRICHONE_PORT:-$DEFAULT_PORT}"
DATA="${ESTRICHONE_DATA_DIR:-$NAME}"
SELF="$REGISTRY/estrichone-installer:$VERSION"

echo "EstrichONE $TITLE $VERSION wird eingerichtet …"
docker pull -q "$IMAGE" >/dev/null 2>&1 || docker image inspect "$IMAGE" >/dev/null 2>&1 || fail "Image $IMAGE nicht verfügbar."

# Adresse dieses Rechners im Netz (für die Anzeige und das Zertifikat); per -e ESTRICHONE_ADDRESS überschreibbar.
ADDRESS="${ESTRICHONE_ADDRESS:-}"
if [ -z "$ADDRESS" ]; then
  ADDRESS="$(docker run --rm --network host --entrypoint ip "$SELF" -4 route get 1.1.1.1 2>/dev/null \
    | sed -n 's/.* src \([0-9.]*\).*/\1/p' | head -n 1 || true)"
fi

if [ "${ESTRICHONE_DATA_DIR:-}" != "" ]; then
  # Ordner auf dem Rechner: dem Dienstbenutzer (UID 1654) übergeben.
  docker run --rm -v "$DATA:/data" --entrypoint sh "$SELF" -c 'chown 1654:1654 /data && chmod 750 /data' \
    || fail "Ordner $DATA nicht nutzbar."
fi

if docker container inspect "$NAME" >/dev/null 2>&1; then
  echo "Vorhandene Installation gefunden – Daten, Zertifikat und Kopplungen bleiben erhalten."
  docker rm -f "$NAME" >/dev/null
fi

set -- -d --name "$NAME" --restart unless-stopped -p "$PORT:8443" -v "$DATA:/data" \
  -e "ESTRICHONE_ADDRESS=${ADDRESS}" --read-only --tmpfs /tmp --security-opt no-new-privileges:true --cap-drop ALL
[ -n "${ESTRICHONE_NAME:-}" ] && set -- "$@" -e "ESTRICHONE_NAME=$ESTRICHONE_NAME"
[ -n "${ESTRICHONE_MIN_RETENTION_YEARS:-}" ] && set -- "$@" -e "ESTRICHONE_MIN_RETENTION_YEARS=$ESTRICHONE_MIN_RETENTION_YEARS"
docker run "$@" "$IMAGE" >/dev/null

# Warten, bis der Dienst läuft, dann Kopplungscode ermitteln.
STARTED=""
for _ in $(seq 1 60); do
  if docker logs "$NAME" 2>&1 | grep -q 'läuft auf Port'; then STARTED=1; break; fi
  sleep 1
done
[ -n "$STARTED" ] || { docker logs --tail 30 "$NAME" >&2; fail "$TITLE startet nicht (Protokoll oben)."; }

CODE="$(docker logs "$NAME" 2>&1 | sed -n 's/.*KOPPLUNGSCODE: *\([A-Z0-9-]*\).*/\1/p' | tail -n 1)"
if [ -z "$CODE" ] && [ "${ESTRICHONE_NEW_CODE:-}" = "1" ]; then
  CODE="$(docker exec "$NAME" estrichone-archive pair | sed -n 's/Kopplungscode: *\([A-Z0-9-]*\).*/\1/p')"
fi
FINGERPRINT="$(docker logs "$NAME" 2>&1 | sed -n 's/.*Fingerabdruck: *//p' | tail -n 1)"

echo
echo "======================================================================"
echo " EstrichONE $TITLE läuft (Port $PORT, HTTPS mit eigenem Zertifikat)."
echo
echo "   Adresse:         ${ADDRESS:-<IP-Adresse dieses Rechners>}${PORT:+:$PORT}"
if [ -n "$CODE" ]; then
  echo "   Kopplungscode:   $CODE        (24 Stunden gültig, einmal verwendbar)"
  echo
  echo " In EstrichONE: $WHERE – Adresse und Code eingeben."
else
  echo
  echo " Bereits gekoppelt. Neuen Code (z. B. für eine weitere Installation):"
  echo "   sudo docker exec $NAME estrichone-archive pair"
fi
echo " Fingerabdruck:     $FINGERPRINT"
echo "                    (zeigt EstrichONE nach dem Koppeln ebenfalls an – muss übereinstimmen)"
echo "======================================================================"
echo
echo "Aktualisieren: denselben Befehl erneut ausführen.  Protokoll: sudo docker logs $NAME"
[ "$ROLE" = "archive" ] && echo "Prüfung jetzt: sudo docker exec $NAME estrichone-archive verify"
exit 0
