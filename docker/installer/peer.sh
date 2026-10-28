#!/bin/sh
# EstrichONE-Archivserver bzw. Backup-Ziel auf einem weiteren Rechner einrichten oder aktualisieren – ein Befehl:
#
#   sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest archiv
#   sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock ghcr.io/florians25/estrichone-installer:latest backup-ziel
#
# Optionen (-e …): ESTRICHONE_PORT=8443 (Backup-Ziel: 8444), ESTRICHONE_NAME="Archiv Keller", ESTRICHONE_ADDRESS=192.168.1.50 (sonst automatisch),
#   ESTRICHONE_DATA_DIR=/srv/archiv (Ordner auf dem Rechner statt Docker-Volume), ESTRICHONE_MIN_RETENTION_YEARS=10 (nur Archiv),
#   ESTRICHONE_NEW_CODE=1 (neuen Kopplungscode ausgeben, z. B. für eine weitere EstrichONE-Installation),
#   ESTRICHONE_REMOTE_UPDATE=aus (keine Fernaktualisierung über den EstrichONE-Server, siehe unten).
# Backup-Ziel, das zieht (zuhause hinter NAT/ohne feste IP, Server z. B. im Rechenzentrum – docs/backup.md):
#   -e ESTRICHONE_PULL_FROM=https://estrichone.firma.de -e ESTRICHONE_PULL_CODE=<Code aus dem Backup Center>
#   optional -e ESTRICHONE_PULL_FINGERPRINT=<SHA-256> (eigenes Zertifikat) und -e ESTRICHONE_PULL_INTERVAL_MINUTES=60
# Erneut ausführen = aktualisieren (Daten, Zertifikat und Kopplungen bleiben). Wird die neue Version nicht gesund, läuft
# automatisch wieder die vorige.
#
# Fernaktualisierung: Neben dem Dienst läuft der kleine Updater „estrichone-<rolle>-updater“ (dieses Image). Der Dienst
# legt einen vom EstrichONE-Server signierten Auftrag (nur die Versionsnummer) in das gemeinsame Volume
# „estrichone-<rolle>-update“; der Updater führt dann genau diesen Befehl mit dem Installer der Zielversion aus
# (estrichone-peer updater <rolle>, intern -e ESTRICHONE_UPDATE_ID=…) und schreibt das Ergebnis zurück.
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112
set -eu

VERSION="${INSTALLER_VERSION:?}"
REGISTRY="${ESTRICHONE_REGISTRY:-ghcr.io/florians25}"
JOB_ID="${ESTRICHONE_UPDATE_ID:-}"
PREVIOUS=""
# Einstellungen, die der Updater bei jeder Aktualisierung unverändert weitergibt (-e NAME übernimmt den Wert aus der Umgebung).
SETTINGS="ESTRICHONE_PORT ESTRICHONE_NAME ESTRICHONE_ADDRESS ESTRICHONE_DATA_DIR ESTRICHONE_MIN_RETENTION_YEARS ESTRICHONE_PULL_FROM
  ESTRICHONE_PULL_CODE ESTRICHONE_PULL_FINGERPRINT ESTRICHONE_PULL_INTERVAL_MINUTES ESTRICHONE_CHECKIN_SECONDS ESTRICHONE_NETWORK
  ESTRICHONE_REGISTRY ESTRICHONE_HEALTH_TIMEOUT"

# Ergebnis für den EstrichONE-Server (nur bei einer Fernaktualisierung): result <status> <meldung> [version]
result() {
  [ -n "$JOB_ID" ] && [ -d /update ] || return 0
  printf 'id=%s\nstatus=%s\nversion=%s\nprevious=%s\nmessage=%s\n' "$JOB_ID" "$1" "${3:-$VERSION}" "$PREVIOUS" "$2" > /update/result.tmp
  chmod 644 /update/result.tmp && mv -f /update/result.tmp /update/result
}
fail() { result failed "$*"; echo "FEHLER: $*" >&2; exit 1; }
env_flags() { for v in $SETTINGS; do eval "val=\${$v:-}"; [ -z "$val" ] || printf ' -e %s' "$v"; done; }
valid_version() {
  [ "${#1}" -le 64 ] && printf '%s' "$1" | grep -Eq '^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
}
exists() { docker container inspect "$1" >/dev/null 2>&1; }
# Gesund = läuft, startet nicht ständig neu und besteht zweimal hintereinander den eingebauten Gesundheitstest.
healthy() { # healthy <container> <sekunden>
  deadline=$(( $(date +%s) + $2 )); ok=0
  while [ "$(date +%s)" -lt "$deadline" ]; do
    if [ "$(docker container inspect -f '{{.State.Running}} {{.State.Restarting}}' "$1" 2>/dev/null)" = "true false" ] \
      && docker exec "$1" estrichone-archive healthcheck >/dev/null 2>&1; then
      ok=$((ok + 1)); [ "$ok" -ge 2 ] && return 0
    else
      ok=0
      [ "$(docker container inspect -f '{{.RestartCount}}' "$1" 2>/dev/null || echo 0)" -lt 3 ] || return 1
    fi
    sleep 3
  done
  return 1
}

MODE=install
if [ "${1:-}" = "updater" ]; then MODE=updater; shift; fi
ROLE_ARG="${1:-}"
case "$ROLE_ARG" in
  archiv|archive) ROLE=archive; TITLE="Archivserver"; WHERE="System → Archiv → „Archivserver koppeln“" ;;
  backup-ziel|backup-target) ROLE=backup-target; TITLE="Backup-Ziel"; WHERE="System → Backup-Ziele → „Backup-Ziel koppeln“" ;;
  *) fail "Unbekannte Rolle '${1:-}' (archiv oder backup-ziel)." ;;
esac

[ -S /var/run/docker.sock ] || fail "Docker-Socket fehlt: bitte mit -v /var/run/docker.sock:/var/run/docker.sock starten."
docker version >/dev/null 2>&1 || fail "Docker ist über den Socket nicht erreichbar."

NAME="estrichone-$ROLE"
IMAGE="$REGISTRY/estrichone-$ROLE:$VERSION"
UPDATE_VOLUME="$NAME-update"
UPDATER="$NAME-updater"
JOB="$NAME-updatejob"
SELF="$REGISTRY/estrichone-installer:$VERSION"
image_version() { docker container inspect -f '{{.Config.Image}}' "$1" 2>/dev/null | sed 's/.*://'; }

# ---------------------------------------------------------------------------------------------------------------------
# Updater (läuft dauerhaft neben dem Dienst): Lebenszeichen schreiben, Aufträge ausführen, Abbrüche melden.
if [ "$MODE" = "updater" ]; then
  [ -d /update ] || fail "Volume $UPDATE_VOLUME ist nicht eingebunden (-v $UPDATE_VOLUME:/update)."
  chown 1654:1654 /update && chmod 755 /update
  write() { # write <id> <status> <meldung>
    printf 'id=%s\nstatus=%s\nversion=%s\nprevious=%s\nmessage=%s\n' "$1" "$2" "$(image_version "$NAME")" "$(image_version "$NAME")" "$3" > /update/result.tmp
    chmod 644 /update/result.tmp && mv -f /update/result.tmp /update/result
    echo "Auftrag $1: $2 – $3"
  }
  echo "Updater für $TITLE (Installer $VERSION) wartet auf Aufträge des EstrichONE-Servers …"
  while true; do
    printf 'version=%s\n' "$VERSION" > /update/updater.tmp && chmod 644 /update/updater.tmp && mv -f /update/updater.tmp /update/updater
    id="$(sed -n 's/^id=\([0-9a-f]\{32\}\)$/\1/p' /update/request 2>/dev/null | head -n 1)"
    started="$(cat /update/started 2>/dev/null || true)"
    finished="$(sed -n 's/^id=//p' /update/result 2>/dev/null || true)"
    if [ -n "$id" ] && [ "$id" != "$started" ] && [ "$id" != "$finished" ]; then
      target="$(sed -n 's/^version=//p' /update/request | head -n 1)"
      echo "$id" > /update/started && chmod 644 /update/started
      if ! valid_version "$target"; then
        write "$id" failed "Ungültige Version im Auftrag."
      else
        installer="$REGISTRY/estrichone-installer:$target"
        echo "Auftrag $id: Aktualisierung auf $target …"
        if ! docker pull -q "$installer" >/dev/null 2>&1 && ! docker image inspect "$installer" >/dev/null 2>&1; then
          write "$id" failed "Installer $installer nicht verfügbar (Internet bzw. Registry prüfen)."
        else
          docker rm -f "$JOB" >/dev/null 2>&1 || true
          # shellcheck disable=SC2046 # env_flags liefert nur feste Namen (-e NAME), Werte kommen aus der Umgebung
          docker run -d --name "$JOB" -v /var/run/docker.sock:/var/run/docker.sock -v "$UPDATE_VOLUME:/update" $(env_flags) \
            -e ESTRICHONE_UPDATE_ID="$id" "$installer" "$ROLE_ARG" >/dev/null \
            || write "$id" failed "Aktualisierung ließ sich nicht starten."
        fi
      fi
    elif [ -n "$started" ] && [ "$started" != "$finished" ] \
      && [ "$(docker container inspect -f '{{.State.Running}}' "$JOB" 2>/dev/null || echo false)" = "false" ]; then
      # Auftrag begonnen, Ausführung beendet, aber kein Ergebnis: abgebrochen.
      sleep 5
      if [ "$started" != "$(sed -n 's/^id=//p' /update/result 2>/dev/null || true)" ]; then
        write "$started" failed "Aktualisierung abgebrochen: $(docker logs --tail 3 "$JOB" 2>&1 | tr '\n' ' ' | cut -c1-300)"
      fi
    fi
    sleep 10
  done
fi

# ---------------------------------------------------------------------------------------------------------------------
# Einrichten bzw. aktualisieren (auch als Auftrag des Updaters)
PORT="${ESTRICHONE_PORT:-8443}"; [ "$ROLE" = "backup-target" ] && PORT="${ESTRICHONE_PORT:-8444}"
DATA="${ESTRICHONE_DATA_DIR:-$NAME}"
HEALTH_TIMEOUT="${ESTRICHONE_HEALTH_TIMEOUT:-120}"
PREVIOUS="$(image_version "$NAME" || true)"

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
# Gemeinsames Verzeichnis für Aufträge und Ergebnisse (gehört dem Dienstbenutzer, der Updater liest es).
docker run --rm -v "$UPDATE_VOLUME:/update" --entrypoint sh "$SELF" -c 'chown 1654:1654 /update && chmod 755 /update' \
  || fail "Volume $UPDATE_VOLUME nicht nutzbar."

# Vorige Version nur anhalten und umbenennen – sie läuft wieder, wenn die neue nicht gesund wird.
docker rm -f "$NAME-vorher" >/dev/null 2>&1 || true
if exists "$NAME"; then
  echo "Vorhandene Installation gefunden (Version ${PREVIOUS:-?}) – Daten, Zertifikat und Kopplungen bleiben erhalten."
  docker stop -t 20 "$NAME" >/dev/null
  docker rename "$NAME" "$NAME-vorher"
fi

set -- -d --name "$NAME" --restart unless-stopped -p "$PORT:8443" -v "$DATA:/data" -v "$UPDATE_VOLUME:/update" \
  -e "ESTRICHONE_ADDRESS=${ADDRESS}" --read-only --tmpfs /tmp --security-opt no-new-privileges:true --cap-drop ALL
[ -n "${ESTRICHONE_NAME:-}" ] && set -- "$@" -e "ESTRICHONE_NAME=$ESTRICHONE_NAME"
[ -n "${ESTRICHONE_MIN_RETENTION_YEARS:-}" ] && set -- "$@" -e "ESTRICHONE_MIN_RETENTION_YEARS=$ESTRICHONE_MIN_RETENTION_YEARS"
[ -n "${ESTRICHONE_NETWORK:-}" ] && set -- "$@" --network "$ESTRICHONE_NETWORK"
if [ "$ROLE" = "backup-target" ] && [ -n "${ESTRICHONE_PULL_FROM:-}" ]; then
  set -- "$@" -e "ESTRICHONE_PULL_FROM=$ESTRICHONE_PULL_FROM"
  [ -n "${ESTRICHONE_PULL_CODE:-}" ] && set -- "$@" -e "ESTRICHONE_PULL_CODE=$ESTRICHONE_PULL_CODE"
  [ -n "${ESTRICHONE_PULL_FINGERPRINT:-}" ] && set -- "$@" -e "ESTRICHONE_PULL_FINGERPRINT=$ESTRICHONE_PULL_FINGERPRINT"
  [ -n "${ESTRICHONE_PULL_INTERVAL_MINUTES:-}" ] && set -- "$@" -e "ESTRICHONE_PULL_INTERVAL_MINUTES=$ESTRICHONE_PULL_INTERVAL_MINUTES"
  [ -n "${ESTRICHONE_CHECKIN_SECONDS:-}" ] && set -- "$@" -e "ESTRICHONE_CHECKIN_SECONDS=$ESTRICHONE_CHECKIN_SECONDS"
fi

if ! docker run "$@" "$IMAGE" >/dev/null || ! healthy "$NAME" "$HEALTH_TIMEOUT"; then
  echo "Die neue Version $VERSION wird nicht gesund. Protokoll:" >&2
  docker logs --tail 20 "$NAME" >&2 2>&1 || true
  docker rm -f "$NAME" >/dev/null 2>&1 || true
  if exists "$NAME-vorher"; then
    docker rename "$NAME-vorher" "$NAME"
    docker start "$NAME" >/dev/null
    if healthy "$NAME" "$HEALTH_TIMEOUT"; then
      result rolled_back "Version $VERSION wurde nicht gesund – Version ${PREVIOUS:-?} läuft wieder (Daten unverändert)." "$PREVIOUS"
      echo "FEHLER: Zurückgerollt – Version ${PREVIOUS:-?} läuft wieder, Daten unverändert." >&2
      exit 1
    fi
    fail "Version $VERSION wurde nicht gesund, und auch die vorige Version ${PREVIOUS:-?} startet nicht (sudo docker logs $NAME)."
  fi
  fail "$TITLE startet nicht (Protokoll oben)."
fi
docker rm -f "$NAME-vorher" >/dev/null 2>&1 || true
result succeeded "Version $VERSION läuft und ist gesund."

CODE="$(docker logs "$NAME" 2>&1 | sed -n 's/.*KOPPLUNGSCODE: *\([A-Z0-9-]*\).*/\1/p' | tail -n 1)"
if [ -z "$CODE" ] && [ "${ESTRICHONE_NEW_CODE:-}" = "1" ]; then
  CODE="$(docker exec "$NAME" estrichone-archive pair | sed -n 's/Kopplungscode: *\([A-Z0-9-]*\).*/\1/p')"
fi
FINGERPRINT="$(docker logs "$NAME" 2>&1 | sed -n 's/.*Fingerabdruck: *//p' | tail -n 1)"

# Updater (neu) starten – zuletzt, denn bei einer Fernaktualisierung ersetzt dieser Lauf auch den Updater selbst.
docker rm -f "$UPDATER" >/dev/null 2>&1 || true
case "${ESTRICHONE_REMOTE_UPDATE:-an}" in
  aus|off|0|false) REMOTE="aus (ESTRICHONE_REMOTE_UPDATE=aus)" ;;
  *)
    # shellcheck disable=SC2046 # env_flags liefert nur feste Namen (-e NAME), Werte kommen aus der Umgebung
    docker run -d --name "$UPDATER" --restart unless-stopped -v /var/run/docker.sock:/var/run/docker.sock -v "$UPDATE_VOLUME:/update" \
      $(env_flags) --entrypoint estrichone-peer "$SELF" updater "$ROLE_ARG" >/dev/null
    REMOTE="an – im EstrichONE-Server unter $( [ "$ROLE" = "archive" ] && echo "System → Archiv" || echo "System → Backup-Ziele" ) „Aktualisieren“"
    ;;
esac
[ -z "$JOB_ID" ] || exit 0

echo
echo "======================================================================"
echo " EstrichONE $TITLE $VERSION läuft (Port $PORT, HTTPS mit eigenem Zertifikat)."
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
echo " Fernaktualisierung: $REMOTE"
echo "======================================================================"
if [ "$ROLE" = "backup-target" ] && [ -n "${ESTRICHONE_PULL_FROM:-}" ]; then
  echo
  echo " Dieses Backup-Ziel ZIEHT die Sicherungen von $ESTRICHONE_PULL_FROM (ausgehend, keine Portfreigabe nötig)."
  echo " Stand im Backup Center unter „Backup-Ziele, die ziehen“. Die Kopplung oben (Adresse/Code) ist dafür nicht nötig."
fi
echo
echo "Aktualisieren: im EstrichONE-Server per Knopfdruck oder hier denselben Befehl erneut ausführen.  Protokoll: sudo docker logs $NAME"
[ "$ROLE" = "archive" ] && echo "Prüfung jetzt: sudo docker exec $NAME estrichone-archive verify"
exit 0
