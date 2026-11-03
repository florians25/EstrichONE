#!/bin/sh
# EstrichONE aktualisieren – vom Installer nach /opt/estrichone/update.sh gelegt.
#
#   sudo /opt/estrichone/update.sh            neueste veröffentlichte Version (auch Vorabversionen)
#   sudo /opt/estrichone/update.sh 0.1.0      bestimmte Version
#   sudo /opt/estrichone/update.sh --ohne-sicherung [VERSION]   ohne vorherige Sicherung (nicht empfohlen)
#   sudo /opt/estrichone/update.sh --pruefen  nur prüfen, ob es ein (Sicherheits-)Update gibt
#   sudo /opt/estrichone/update.sh --automatik sicherheit|patch|aus [HH:MM-HH:MM]
#        automatische Updates einrichten (stündliche Prüfung per systemd-Timer bzw. /etc/cron.d):
#        sicherheit = nur Releases mit „Sicherheitsupdate“ in den Versionshinweisen, patch = alle Fehlerbehebungen
#        derselben Versionslinie (x.y), jeweils nur im Zeitfenster (Standard 02:00-05:00, Serverzeit).
#
# Ablauf: Version ermitteln → Sicherung → Installer der Zielversion ausführen (Konfiguration und Daten bleiben)
# → warten, bis alle Dienste gesund sind → Version prüfen. Der Stand (geprüft, verfügbar, letzte Automatik) steht in
# data/backup-status/update.json; die Weboberfläche zeigt Administratoren daraus „Sicherheitsupdate verfügbar“.
# shellcheck disable=SC1111,SC1112
set -eu

DIR="$(cd "$(dirname "$0")" && pwd)"
SELF="$DIR/$(basename "$0")"
REPO="${ESTRICHONE_GITHUB_REPO:-florians25/EstrichONE}"
FEED="${ESTRICHONE_RELEASE_FEED:-https://api.github.com/repos/$REPO/releases?per_page=30}"
BACKUP=1
TARGET=""
ACTION=update
AUTO_MODE=""
AUTO_WINDOW=""
while [ $# -gt 0 ]; do
  case "$1" in
    --ohne-sicherung|--no-backup) BACKUP=0 ;;
    --pruefen|--check) ACTION=check ;;
    --automatisch|--auto) ACTION=auto ;;
    --automatik|--auto-setup)
      ACTION=setup; AUTO_MODE="${2:-}"; [ $# -gt 1 ] && shift
      case "${2:-}" in [0-9]*-[0-9]*) AUTO_WINDOW="$2"; shift ;; esac ;;
    -h|--help) sed -n '2,17p' "$0"; exit 0 ;;
    v[0-9]*|[0-9]*) TARGET="${1#v}" ;;
    *) echo "Unbekannte Angabe: $1 (Hilfe: $0 --help)" >&2; exit 2 ;;
  esac
  shift
done

fail() { echo; echo "FEHLER: $*" >&2; exit 1; }
[ "$(id -u)" = "0" ] || fail "Bitte mit sudo ausführen: sudo $0"
[ "$DIR" = "/opt/estrichone" ] || fail "update.sh muss in /opt/estrichone liegen (Installationsverzeichnis des Installers)."
[ -f "$DIR/.env" ] || fail "Keine Installation in $DIR gefunden (.env fehlt)."
command -v docker >/dev/null 2>&1 || fail "Docker ist nicht installiert."
# Die .env enthält Datenbank- und Sicherungspasswort: nur root (ältere Installationen werden hier nachgezogen).
chmod 600 "$DIR/.env" 2>/dev/null || true
env_value() { sed -n "s/^$1=//p" "$DIR/.env" | tail -n 1 | sed 's/^"\(.*\)"$/\1/'; }
set_env() { # set_env SCHLÜSSEL WERT
  if grep -q "^$1=" "$DIR/.env"; then sed -i "s|^$1=.*|$1=$2|" "$DIR/.env"; else echo "$1=$2" >> "$DIR/.env"; fi
}
REGISTRY="$(env_value ESTRICHONE_REGISTRY)"; REGISTRY="${REGISTRY:-ghcr.io/florians25}"
CURRENT="$(env_value ESTRICHONE_VERSION)"
DATA="$(env_value ESTRICHONE_DATA_DIR)"; DATA="${DATA:-./data}"
case "$DATA" in /*) ;; *) DATA="$DIR/${DATA#./}" ;; esac
STATUS_FILE="$DATA/backup-status/update.json"

fetch() {
  case "$1" in
    file://*) cat "${1#file://}"; return ;;
  esac
  if command -v curl >/dev/null 2>&1; then curl -fsSL --max-time 30 -H "Accept: application/vnd.github+json" "$1"
  elif command -v wget >/dev/null 2>&1; then wget -qO- -T 30 "$1"
  else docker run --rm --entrypoint wget "$REGISTRY/estrichone-installer:${CURRENT:-latest}" -qO- -T 30 "$1"
  fi
}

# Releases aus dem Feed (GitHub-API): je Zeile „version vorab(0/1) sicherheit(0/1)“, neueste zuerst, ohne Entwürfe.
# Sicherheitsupdate = Titel oder Versionshinweise enthalten „Sicherheitsupdate“ bzw. „security update“.
releases() {
  fetch "$FEED" 2>/dev/null | awk '
    function flush() { if (tag != "" && draft == 0) print tag, pre, sec; tag = ""; pre = 0; sec = 0; draft = 0 }
    /^    "tag_name": *"v/ { flush(); t = $0; sub(/.*"tag_name": *"v/, "", t); sub(/".*/, "", t); tag = t; next }
    /^    "prerelease": *true/ { pre = 1; next }
    /^    "draft": *true/ { draft = 1; next }
    /^    "(name|body)": / { l = tolower($0); if (l ~ /sicherheitsupdate|security update|\[security\]/) sec = 1; next }
    END { flush() }'
}

# Vergleich nach Semantic Versioning: ver_cmp A B → -1, 0 oder 1 (0.1.0-preview.9 < 0.1.0-preview.10 < 0.1.0 < 0.1.1).
ver_cmp() {
  awk -v a="$1" -v b="$2" '
    function cmpid(x, y,  nx, ny) {
      nx = (x ~ /^[0-9]+$/); ny = (y ~ /^[0-9]+$/)
      if (nx && ny) return (x + 0 < y + 0) ? -1 : (x + 0 > y + 0)
      if (nx != ny) return nx ? -1 : 1
      return (x < y) ? -1 : (x > y)
    }
    function cmp(a, b,  ca, cb, pa, pb, i, n, m, r, xa, xb) {
      ca = a; pa = ""; if (index(a, "-")) { ca = substr(a, 1, index(a, "-") - 1); pa = substr(a, index(a, "-") + 1) }
      cb = b; pb = ""; if (index(b, "-")) { cb = substr(b, 1, index(b, "-") - 1); pb = substr(b, index(b, "-") + 1) }
      split(ca, xa, "."); split(cb, xb, ".")
      for (i = 1; i <= 3; i++) { r = cmpid(xa[i] + 0, xb[i] + 0); if (r) return r }
      if (pa == "" && pb == "") return 0
      if (pa == "") return 1
      if (pb == "") return -1
      n = split(pa, xa, "."); m = split(pb, xb, ".")
      for (i = 1; i <= n && i <= m; i++) { r = cmpid(xa[i], xb[i]); if (r) return r }
      return (n < m) ? -1 : (n > m)
    }
    BEGIN { print cmp(a, b) }'
}
line_of() { echo "$1" | sed 's/-.*//' | cut -d. -f1,2; }

# Neueste passende Version für die Automatik bzw. den Hinweis: dieselbe Versionslinie (x.y) wie installiert, neuer als
# installiert, Vorabversionen nur, wenn bereits eine Vorabversion läuft. Ausgabe: „version sicherheit(0/1)“ oder leer.
candidate() { # candidate RELEASES
  echo "$1" | while read -r version pre sec; do
      [ -n "$version" ] || continue
      [ "$(line_of "$version")" = "$(line_of "$CURRENT")" ] || continue
      [ "$pre" = "0" ] || case "$CURRENT" in *-*) ;; *) continue ;; esac
      [ "$(ver_cmp "$version" "$CURRENT")" = "1" ] || continue
      echo "$version $sec"
    done | {
      best=""; security=0
      while read -r version sec; do
        [ "$sec" = "1" ] && security=1
        if [ -z "$best" ] || [ "$(ver_cmp "$version" "$best")" = "1" ]; then best="$version"; fi
      done
      [ -z "$best" ] || echo "$best $security"
    }
}

in_window() { # in_window HH:MM-HH:MM [JETZT=HH:MM] – Fenster über Mitternacht (22:00-04:00) erlaubt
  minutes() { h="$(echo "$1" | cut -d: -f1 | sed 's/^0//')"; m="$(echo "$1" | cut -d: -f2 | sed 's/^0//')"; echo $(( ${h:-0} * 60 + ${m:-0} )); }
  from="$(minutes "${1%-*}")"; to="$(minutes "${1#*-}")"; now="$(minutes "${2:-$(date +%H:%M)}")"
  if [ "$from" -le "$to" ]; then [ "$now" -ge "$from" ] && [ "$now" -lt "$to" ]
  else [ "$now" -ge "$from" ] || [ "$now" -lt "$to" ]
  fi
}

json_str() { printf '"%s"' "$(printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | tr -d '\n\r')"; }

# Stand für die Weboberfläche (API liest /status/update.json, nur lesend). Bisheriges Ergebnis der Automatik bleibt erhalten.
write_status() { # write_status VERFÜGBAR SICHERHEIT [ERGEBNIS VON NACH MELDUNG]
  [ -d "$DATA/backup-status" ] || return 0
  last=""
  if [ -n "${3:-}" ]; then
    last="{\"at\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"from\":$(json_str "$4"),\"to\":$(json_str "$5"),\"result\":$(json_str "$3"),\"message\":$(json_str "${6:-}")}"
  elif [ -f "$STATUS_FILE" ]; then
    last="$(sed -n 's/.*"lastAutoUpdate":\({[^}]*}\).*/\1/p' "$STATUS_FILE")"
  fi
  mode="$(env_value ESTRICHONE_AUTO_UPDATE)"; window="$(env_value ESTRICHONE_AUTO_UPDATE_WINDOW)"
  tmp="$STATUS_FILE.tmp"
  printf '{"checkedAt":"%s","installed":%s,"available":%s,"securityUpdate":%s,"autoUpdate":%s,"window":%s,"lastAutoUpdate":%s}\n' \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$(json_str "$CURRENT")" "$( [ -n "${1:-}" ] && json_str "$1" || echo null)" \
    "$( [ "${2:-0}" = "1" ] && echo true || echo false)" "$(json_str "${mode:-aus}")" "$(json_str "${window:-02:00-05:00}")" \
    "${last:-null}" > "$tmp" && chmod 644 "$tmp" && mv "$tmp" "$STATUS_FILE"
}

if [ "$ACTION" = "setup" ]; then
  case "$AUTO_MODE" in sicherheit|patch|aus) ;; *) fail "Automatik: sicherheit, patch oder aus angeben (z. B. sudo $0 --automatik sicherheit 02:00-05:00)." ;; esac
  window="${AUTO_WINDOW:-$(env_value ESTRICHONE_AUTO_UPDATE_WINDOW)}"; window="${window:-02:00-05:00}"
  echo "$window" | grep -Eq '^([01][0-9]|2[0-3]):[0-5][0-9]-([01][0-9]|2[0-3]):[0-5][0-9]$' || fail "Zeitfenster als HH:MM-HH:MM angeben, z. B. 02:00-05:00."
  set_env ESTRICHONE_AUTO_UPDATE "$AUTO_MODE"
  set_env ESTRICHONE_AUTO_UPDATE_WINDOW "$window"
  minute="$(awk 'BEGIN { srand(); print int(rand() * 60) }')"
  rm -f /etc/cron.d/estrichone-update
  if [ "$AUTO_MODE" = "aus" ]; then
    if [ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1; then
      systemctl disable --now estrichone-update.timer >/dev/null 2>&1 || true
      rm -f /etc/systemd/system/estrichone-update.timer /etc/systemd/system/estrichone-update.service
      systemctl daemon-reload >/dev/null 2>&1 || true
    fi
    echo "Automatische Updates ausgeschaltet."
  elif [ -d /run/systemd/system ] && command -v systemctl >/dev/null 2>&1; then
    cat > /etc/systemd/system/estrichone-update.service <<EOF
[Unit]
Description=EstrichONE: automatische Sicherheits-/Patch-Updates prüfen und einspielen
After=docker.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=$SELF --automatisch
EOF
    cat > /etc/systemd/system/estrichone-update.timer <<EOF
[Unit]
Description=EstrichONE: stündlich auf Updates prüfen

[Timer]
OnCalendar=*-*-* *:$(printf %02d "$minute"):00
RandomizedDelaySec=120
Persistent=false

[Install]
WantedBy=timers.target
EOF
    systemctl daemon-reload || fail "systemd konnte nicht neu geladen werden."
    systemctl enable --now estrichone-update.timer >/dev/null || fail "systemd-Timer konnte nicht eingerichtet werden."
    echo "Automatische Updates ($AUTO_MODE) eingerichtet: stündliche Prüfung (systemd-Timer estrichone-update.timer), Einspielen nur $window."
    echo "Protokoll: journalctl -u estrichone-update.service"
  elif [ -d /etc/cron.d ]; then
    printf '# EstrichONE: automatische Updates (%s, Zeitfenster %s) – ändern mit: sudo %s --automatik …\n%s * * * * root %s --automatisch >> /var/log/estrichone-update.log 2>&1\n' \
      "$AUTO_MODE" "$window" "$SELF" "$minute" "$SELF" > /etc/cron.d/estrichone-update
    chmod 644 /etc/cron.d/estrichone-update
    echo "Automatische Updates ($AUTO_MODE) eingerichtet: stündliche Prüfung (/etc/cron.d/estrichone-update), Einspielen nur $window."
    echo "Protokoll: /var/log/estrichone-update.log"
  else
    fail "Weder systemd noch /etc/cron.d gefunden – bitte stündlich „$SELF --automatisch“ einplanen."
  fi
  write_status "" 0
  exit 0
fi

if [ "$ACTION" = "check" ] || [ "$ACTION" = "auto" ]; then
  list="$(releases || true)"
  if [ -z "$list" ]; then
    [ "$ACTION" = "auto" ] && { echo "$(date '+%F %T') Release-Feed nicht erreichbar – nächster Versuch beim nächsten Lauf."; exit 0; }
    fail "Release-Feed nicht erreichbar (Internet?)."
  fi
  found="$(candidate "$list")"
  version="${found% *}"; security="${found#* }"
  [ -n "$found" ] || { version=""; security=0; }
  if [ "$ACTION" = "check" ]; then
    write_status "$version" "$security"
    if [ -z "$version" ]; then echo "Kein Update in der Versionslinie $(line_of "$CURRENT") – installiert ist $CURRENT."; exit 0; fi
    [ "$security" = "1" ] && echo "SICHERHEITSUPDATE verfügbar: $CURRENT → $version" || echo "Update verfügbar: $CURRENT → $version"
    echo "Einspielen: sudo $SELF $version"
    exit 0
  fi
  mode="$(env_value ESTRICHONE_AUTO_UPDATE)"; mode="${mode:-aus}"
  window="$(env_value ESTRICHONE_AUTO_UPDATE_WINDOW)"; window="${window:-02:00-05:00}"
  if [ -z "$version" ] || [ "$mode" = "aus" ] || { [ "$mode" = "sicherheit" ] && [ "$security" != "1" ]; }; then
    write_status "$version" "$security"; exit 0
  fi
  if ! in_window "$window" "${ESTRICHONE_UPDATE_NOW:-}"; then
    write_status "$version" "$security"
    echo "$(date '+%F %T') Update $CURRENT → $version wartet auf das Zeitfenster $window."
    exit 0
  fi
  if [ "${ESTRICHONE_UPDATE_DRY_RUN:-}" = "1" ]; then echo "AUTOMATIK: $CURRENT → $version"; exit 0; fi
  # Nur ein Lauf gleichzeitig.
  LOCK=/run/estrichone-update.lock
  mkdir "$LOCK" 2>/dev/null || { echo "Ein Update läuft bereits."; exit 0; }
  trap 'rmdir "$LOCK" 2>/dev/null || true' EXIT
  echo "$(date '+%F %T') Automatisches Update ($mode): $CURRENT → $version"
  before="$CURRENT"
  if "$SELF" "$version"; then
    CURRENT="$version"
    write_status "" 0 succeeded "$before" "$version" "Automatisch aktualisiert auf $version."
  else
    write_status "$version" "$security" failed "$before" "$version" "Automatisches Update auf $version fehlgeschlagen – Protokoll auf dem Server prüfen, manuell: sudo $SELF $version"
    exit 1
  fi
  exit 0
fi

if [ -z "$TARGET" ]; then
  echo "Neueste Version ermitteln …"
  TARGET="$(releases | head -n 1 | cut -d' ' -f1)" || true
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
CURRENT_BEFORE="$CURRENT"; CURRENT="$TARGET"; write_status "" 0 2>/dev/null || true
echo
echo "======================================================================"
echo " Update erfolgreich: EstrichONE ${CURRENT_BEFORE:-?} → $TARGET (Server meldet ${RUNNING:-?})."
echo "======================================================================"
