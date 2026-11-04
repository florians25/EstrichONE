#!/bin/sh
# EstrichONE-Kombiserver: Backup-Ziel UND Archivserver auf EINEM Rechner – ein Befehl, ein Kopplungscode:
#
#   sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v /opt/estrichone-kombi:/opt/estrichone-kombi \
#     ghcr.io/florians25/estrichone-installer:<version> backup-archiv
#
# Modusname „backup-archiv“ (Aliase: backup-archive, kombi, kombiserver): sagt, was installiert wird – beide Dienste.
# Getrennt bleiben: zwei Container (estrichone-backup-target, estrichone-archive), zwei Datenbereiche, zwei Updater,
# zwei Zertifikate. Gemeinsam sind nur das Verzeichnis /opt/estrichone-kombi mit EINER .env (Rechte 600) und der Code.
#
# Optionen (-e …), werden in /opt/estrichone-kombi/.env gemerkt (erneut ausführen = aktualisieren mit denselben Werten):
#   ESTRICHONE_NAME="Kombiserver Keller"        Anzeigename (die Dienste heißen „… – Backup-Ziel“ bzw. „… – Archiv“)
#   ESTRICHONE_ADDRESS=192.168.1.50             Adresse für Anzeige und Zertifikat (sonst automatisch)
#   ESTRICHONE_BACKUP_PORT=8444 / ESTRICHONE_ARCHIVE_PORT=8443   Ports der beiden Dienste (Standard wie einzeln)
#   ESTRICHONE_BACKUP_DATA_DIR=/srv/backup / ESTRICHONE_ARCHIVE_DATA_DIR=/srv/archiv   Ordner statt Docker-Volumes
#                                               (müssen verschieden sein und dürfen nicht ineinander liegen)
#   ESTRICHONE_BIND_ADDRESS=127.0.0.1           nur an diese Adresse binden (Reverse-Proxy mit TLS-Durchleitung davor)
#   ESTRICHONE_MIN_RETENTION_YEARS=10           Mindestfrist des Archivs
#   ESTRICHONE_PULL_FROM=https://… -e ESTRICHONE_PULL_CODE=…   Backup-Ziel zieht (Kombiserver hinter NAT, Topologie A);
#                                               der Code aus dem Backup Center koppelt dann auch das Archiv
#   ESTRICHONE_NEW_CODE=1                       neuen Kopplungscode für beide Dienste (z. B. nach dem Entkoppeln)
#   ESTRICHONE_REMOTE_UPDATE=aus                keine Fernaktualisierung
# Ein vorhandenes Backup-Ziel bzw. ein vorhandener Archivserver auf diesem Rechner wird übernommen (Daten, Kopplung,
# Port und Datenordner bleiben) und um den anderen Dienst ergänzt.
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112
set -eu

VERSION="${INSTALLER_VERSION:?}"
DIR=/opt/estrichone-kombi
ENV_FILE="$DIR/.env"
ALPHABET='ABCDEFGHJKLMNPQRSTUVWXYZ23456789'

fail() { echo "FEHLER: $*" >&2; exit 1; }
exists() { docker container inspect "$1" >/dev/null 2>&1; }
set_env() { # set_env SCHLÜSSEL WERT – ersetzt oder ergänzt eine Zeile (Werte ohne Zeilenumbruch, in Anführungszeichen)
  value="$(printf '%s' "$2" | sed 's/["\\$`]//g')"
  tmp="$ENV_FILE.tmp"
  (umask 077 && { grep -v "^$1=" "$ENV_FILE" 2>/dev/null || true; printf '%s="%s"\n' "$1" "$value"; } > "$tmp")
  mv -f "$tmp" "$ENV_FILE"
}
del_env() { tmp="$ENV_FILE.tmp"; (umask 077 && { grep -v "^$1=" "$ENV_FILE" 2>/dev/null || true; } > "$tmp"); mv -f "$tmp" "$ENV_FILE"; }
saved() { sed -n "s/^$1=\"\(.*\)\"$/\1/p" "$ENV_FILE" 2>/dev/null | tail -n 1; }
# Einstellungen eines vorhandenen Containers (übernehmen statt überschreiben)
container_port() { docker port "$1" 8443/tcp 2>/dev/null | head -n 1 | sed 's/.*://'; }
container_bind() { docker port "$1" 8443/tcp 2>/dev/null | head -n 1 | sed -n 's/^\([0-9.]*\):[0-9]*$/\1/p' | grep -v '^0\.0\.0\.0$' || true; }
container_data() { # Ordner auf dem Rechner bzw. leer (Standard-Volume)
  src="$(docker container inspect -f '{{range .Mounts}}{{if eq .Destination "/data"}}{{.Type}} {{if eq .Type "volume"}}{{.Name}}{{else}}{{.Source}}{{end}}{{end}}{{end}}' "$1" 2>/dev/null || true)"
  case "$src" in "volume $1"|"") ;; "volume "*) echo "${src#volume }" ;; *) echo "${src#* }" ;; esac
}
container_env() { docker container inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$1" 2>/dev/null | sed -n "s/^$2=//p" | head -n 1; }
# pick NAME VORHANDEN STANDARD: Umgebung > .env > vorhandener Container > Standard
pick() {
  eval "env_value=\${$1:-}"
  if [ -n "$env_value" ]; then printf '%s' "$env_value"; return; fi
  stored="$(saved "$1")"
  if [ -n "$stored" ] || grep -q "^$1=" "$ENV_FILE" 2>/dev/null; then printf '%s' "$stored"; return; fi
  if [ -n "$2" ]; then printf '%s' "$2"; else printf '%s' "$3"; fi
}
new_code() { # 16 Zeichen aus 32 (80 Bit), gleichverteilt (tr verwirft alles außerhalb des Alphabets)
  raw="$(tr -dc "$ALPHABET" < /dev/urandom | head -c 16)"
  [ "${#raw}" -eq 16 ] || fail "Zufallszahlen nicht verfügbar."
  printf '%s-%s-%s-%s' "$(echo "$raw" | cut -c1-4)" "$(echo "$raw" | cut -c5-8)" "$(echo "$raw" | cut -c9-12)" "$(echo "$raw" | cut -c13-16)"
}

[ -S /var/run/docker.sock ] || fail "Docker-Socket fehlt: bitte mit -v /var/run/docker.sock:/var/run/docker.sock starten."
docker version >/dev/null 2>&1 || fail "Docker ist über den Socket nicht erreichbar."
{ [ -d "$DIR" ] && touch "$DIR/.schreibtest" 2>/dev/null && rm -f "$DIR/.schreibtest"; } \
  || fail "Bitte das Verzeichnis des Kombiservers einbinden: -v $DIR:$DIR (gleicher Pfad innen und außen)."
[ -f "$ENV_FILE" ] || (umask 077 && printf '# EstrichONE-Kombiserver (Backup-Ziel + Archiv) – vom Installer verwaltet, nur für root lesbar.\n' > "$ENV_FILE")
chmod 600 "$ENV_FILE"

B=estrichone-backup-target
A=estrichone-archive
echo "EstrichONE-Kombiserver $VERSION (Backup-Ziel + Archivserver) wird eingerichtet …"
exists "$B" && echo "Vorhandenes Backup-Ziel gefunden – Daten, Kopplung, Port und Ordner bleiben erhalten."
exists "$A" && echo "Vorhandener Archivserver gefunden – Daten, Kopplung, Port und Ordner bleiben erhalten."

NAME="$(pick ESTRICHONE_NAME "" "EstrichONE-Kombiserver")"
ADDRESS="$(pick ESTRICHONE_ADDRESS "" "")"
BACKUP_PORT="$(pick ESTRICHONE_BACKUP_PORT "$(container_port "$B")" 8444)"
ARCHIVE_PORT="$(pick ESTRICHONE_ARCHIVE_PORT "$(container_port "$A")" 8443)"
BACKUP_DATA="$(pick ESTRICHONE_BACKUP_DATA_DIR "$(container_data "$B")" "")"
ARCHIVE_DATA="$(pick ESTRICHONE_ARCHIVE_DATA_DIR "$(container_data "$A")" "")"
BIND="$(pick ESTRICHONE_BIND_ADDRESS "$(container_bind "$B")" "")"
RETENTION="$(pick ESTRICHONE_MIN_RETENTION_YEARS "$(container_env "$A" ESTRICHONE_MIN_RETENTION_YEARS)" "")"
PULL_FROM="$(pick ESTRICHONE_PULL_FROM "$(container_env "$B" ESTRICHONE_PULL_FROM)" "")"
PULL_FINGERPRINT="$(pick ESTRICHONE_PULL_FINGERPRINT "$(container_env "$B" ESTRICHONE_PULL_FINGERPRINT)" "")"
PULL_INTERVAL="$(pick ESTRICHONE_PULL_INTERVAL_MINUTES "$(container_env "$B" ESTRICHONE_PULL_INTERVAL_MINUTES)" "")"
REMOTE="$(pick ESTRICHONE_REMOTE_UPDATE "" an)"

for port in "$BACKUP_PORT" "$ARCHIVE_PORT"; do
  case "$port" in ''|*[!0-9]*) fail "Ungültiger Port: $port" ;; esac
  if [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then fail "Ungültiger Port: $port"; fi
done
[ "$BACKUP_PORT" != "$ARCHIVE_PORT" ] || fail "Backup-Ziel und Archiv brauchen verschiedene Ports (Standard 8444 und 8443)."
case "$NAME" in *'"'*|*'$'*|*'`'*|*"\\"*) fail "Der Name darf keine Anführungszeichen, \$, \` oder \\ enthalten." ;; esac
for dir in "$BACKUP_DATA" "$ARCHIVE_DATA"; do
  case "$dir" in ""|/*) ;; *) fail "Datenordner bitte als absoluten Pfad angeben: $dir" ;; esac
  case "$dir" in "$DIR"|"$DIR"/*) fail "Datenordner nicht in $DIR (dort liegt nur die Konfiguration): $dir" ;; esac
done
# Keine Vermischung von Backup- und Archivdaten: getrennte, nicht ineinander liegende Ordner.
if [ -n "$BACKUP_DATA" ] && [ -n "$ARCHIVE_DATA" ]; then
  b="${BACKUP_DATA%/}/"; a="${ARCHIVE_DATA%/}/"
  case "$a" in "$b"*) fail "Archivordner liegt im Backup-Ordner – bitte getrennte Ordner verwenden." ;; esac
  case "$b" in "$a"*) fail "Backup-Ordner liegt im Archivordner – bitte getrennte Ordner verwenden." ;; esac
fi

# Konfiguration merken (der Kopplungscode des Backup Centers wird nicht gespeichert – er gilt nur einmal).
set_env ESTRICHONE_VERSION "$VERSION"
set_env ESTRICHONE_NAME "$NAME"
set_env ESTRICHONE_ADDRESS "$ADDRESS"
set_env ESTRICHONE_BACKUP_PORT "$BACKUP_PORT"
set_env ESTRICHONE_ARCHIVE_PORT "$ARCHIVE_PORT"
set_env ESTRICHONE_BACKUP_DATA_DIR "$BACKUP_DATA"
set_env ESTRICHONE_ARCHIVE_DATA_DIR "$ARCHIVE_DATA"
set_env ESTRICHONE_BIND_ADDRESS "$BIND"
set_env ESTRICHONE_MIN_RETENTION_YEARS "$RETENTION"
set_env ESTRICHONE_PULL_FROM "$PULL_FROM"
set_env ESTRICHONE_PULL_FINGERPRINT "$PULL_FINGERPRINT"
set_env ESTRICHONE_PULL_INTERVAL_MINUTES "$PULL_INTERVAL"
set_env ESTRICHONE_REMOTE_UPDATE "$REMOTE"
chmod 600 "$ENV_FILE"

# Beide Dienste nacheinander mit dem bewährten Einzel-Installer (gleiche Container, Volumes, Updater, Rücksprung).
# Jeder Dienst bekommt nur seine eigenen Werte – der Updater gibt sie bei Fernaktualisierungen unverändert weiter.
run_role() { # run_role <rolle> <log> VAR=WERT …
  role="$1"; log="$2"; shift 2
  if ! env -u ESTRICHONE_PORT -u ESTRICHONE_DATA_DIR -u ESTRICHONE_PULL_FROM -u ESTRICHONE_PULL_CODE -u ESTRICHONE_PULL_FINGERPRINT \
      -u ESTRICHONE_PULL_INTERVAL_MINUTES -u ESTRICHONE_MIN_RETENTION_YEARS -u ESTRICHONE_NEW_CODE -u ESTRICHONE_NAME \
      -u ESTRICHONE_ADDRESS -u ESTRICHONE_BIND_ADDRESS -u ESTRICHONE_REMOTE_UPDATE "$@" estrichone-peer "$role" > "$log" 2>&1; then
    cat "$log" >&2
    fail "Einrichtung „$role“ fehlgeschlagen (Meldung oben). Der andere Dienst ist unverändert."
  fi
}
LOGS="$(mktemp -d)"
set -- ESTRICHONE_PORT="$BACKUP_PORT" ESTRICHONE_NAME="$NAME – Backup-Ziel" ESTRICHONE_REMOTE_UPDATE="$REMOTE"
[ -n "$ADDRESS" ] && set -- "$@" ESTRICHONE_ADDRESS="$ADDRESS"
[ -n "$BIND" ] && set -- "$@" ESTRICHONE_BIND_ADDRESS="$BIND"
[ -n "$BACKUP_DATA" ] && set -- "$@" ESTRICHONE_DATA_DIR="$BACKUP_DATA"
if [ -n "$PULL_FROM" ]; then
  set -- "$@" ESTRICHONE_PULL_FROM="$PULL_FROM"
  [ -n "${ESTRICHONE_PULL_CODE:-}" ] && set -- "$@" ESTRICHONE_PULL_CODE="$ESTRICHONE_PULL_CODE"
  [ -n "$PULL_FINGERPRINT" ] && set -- "$@" ESTRICHONE_PULL_FINGERPRINT="$PULL_FINGERPRINT"
  [ -n "$PULL_INTERVAL" ] && set -- "$@" ESTRICHONE_PULL_INTERVAL_MINUTES="$PULL_INTERVAL"
fi
echo "1/2 Backup-Ziel (Port $BACKUP_PORT) …"
run_role backup-ziel "$LOGS/backup.log" "$@"

set -- ESTRICHONE_PORT="$ARCHIVE_PORT" ESTRICHONE_NAME="$NAME – Archiv" ESTRICHONE_REMOTE_UPDATE="$REMOTE"
[ -n "$ADDRESS" ] && set -- "$@" ESTRICHONE_ADDRESS="$ADDRESS"
[ -n "$BIND" ] && set -- "$@" ESTRICHONE_BIND_ADDRESS="$BIND"
[ -n "$ARCHIVE_DATA" ] && set -- "$@" ESTRICHONE_DATA_DIR="$ARCHIVE_DATA"
[ -n "$RETENTION" ] && set -- "$@" ESTRICHONE_MIN_RETENTION_YEARS="$RETENTION"
echo "2/2 Archivserver (Port $ARCHIVE_PORT) …"
run_role archiv "$LOGS/archive.log" "$@"

value() { sed -n "s/.*$2: *//p" "$1" | head -n 1 | sed 's/ *(.*//; s/ *$//'; }
SHOWN_ADDRESS="$(sed -n 's/.*Adresse: *\([^:]*\):.*/\1/p' "$LOGS/backup.log" | head -n 1)"
BACKUP_FP="$(value "$LOGS/backup.log" Fingerabdruck)"
ARCHIVE_FP="$(value "$LOGS/archive.log" Fingerabdruck)"
B_OPEN="$(value "$LOGS/backup.log" Kopplungscode)"
A_OPEN="$(value "$LOGS/archive.log" Kopplungscode)"
B_REMOTE="$(value "$LOGS/backup.log" Fernaktualisierung)"
A_REMOTE="$(value "$LOGS/archive.log" Fernaktualisierung)"
rm -rf "$LOGS"

# EIN Code für beide Dienste: jeder Dienst, der noch auf eine Kopplung wartet (oder mit ESTRICHONE_NEW_CODE=1),
# bekommt denselben Code. Zieht das Backup-Ziel (Topologie A), ist es der Code aus dem Backup Center.
NEED_B=0; NEED_A=0
if [ -z "$PULL_FROM" ] && { [ -n "$B_OPEN" ] || [ "${ESTRICHONE_NEW_CODE:-}" = "1" ]; }; then NEED_B=1; fi
if [ -n "$A_OPEN" ] || [ "${ESTRICHONE_NEW_CODE:-}" = "1" ]; then NEED_A=1; fi
CODE=""
if [ "$NEED_B" = 1 ] || [ "$NEED_A" = 1 ]; then
  if [ -n "$PULL_FROM" ] && [ -n "${ESTRICHONE_PULL_CODE:-}" ]; then
    CODE="$ESTRICHONE_PULL_CODE"
  elif [ "${ESTRICHONE_NEW_CODE:-}" != "1" ] && [ -n "$(saved ESTRICHONE_KOMBI_CODE)" ]; then
    CODE="$(saved ESTRICHONE_KOMBI_CODE)"   # erneut ausgeführt, noch nicht (ganz) gekoppelt: derselbe Code
  else
    CODE="$(new_code)"
  fi
  CODE="$(printf '%s' "$CODE" | tr '[:lower:]' '[:upper:]')"
  services=""
  [ "$NEED_B" = 1 ] && services="$B"
  [ "$NEED_A" = 1 ] && services="$services $A"
  for svc in $services; do
    docker exec "$svc" estrichone-archive pair "$CODE" >/dev/null || fail "Kopplungscode für $svc nicht setzbar."
  done
  set_env ESTRICHONE_KOMBI_CODE "$CODE"
else
  del_env ESTRICHONE_KOMBI_CODE
fi
chmod 600 "$ENV_FILE"

HOST="${ADDRESS:-${SHOWN_ADDRESS:-<IP-Adresse dieses Rechners>}}"
echo
echo "======================================================================"
echo " EstrichONE-Kombiserver $VERSION läuft: Backup-Ziel und Archivserver (getrennte Container und Daten)."
echo
echo "   Adresse:            $HOST"
echo "   Backup-Ziel:        $HOST:$BACKUP_PORT$( [ -n "$PULL_FROM" ] && echo "   (zieht von $PULL_FROM – keine Portfreigabe nötig)" )"
echo "   Archivserver:       $HOST:$ARCHIVE_PORT"
if [ -n "$CODE" ]; then
  echo "   Kopplungscode:      $CODE        (24 Stunden gültig, einmal je Dienst)"
  echo
  if [ -n "$PULL_FROM" ]; then
    echo " In EstrichONE: System → Backup-Ziele → „Kombiserver koppeln“ → „Kombiserver zieht“ → Schritt 2: Adresse des"
    echo " Archivs eingeben (Port $ARCHIVE_PORT am Router weiterleiten oder Tunnel-Adresse) – der Code ist schon bekannt."
  else
    echo " In EstrichONE: System → Backup-Ziele → „Kombiserver koppeln“ – Adresse und Code eingeben (koppelt beide Dienste)."
  fi
else
  echo
  echo " Beide Dienste sind bereits gekoppelt. Neuer Code für beide: denselben Befehl mit -e ESTRICHONE_NEW_CODE=1."
fi
echo " Fingerabdruck Backup-Ziel: $BACKUP_FP"
echo " Fingerabdruck Archiv:      $ARCHIVE_FP"
echo "                    (zeigt EstrichONE nach dem Koppeln ebenfalls an – müssen übereinstimmen)"
echo " Fernaktualisierung: Backup-Ziel ${B_REMOTE%% –*}, Archiv ${A_REMOTE%% –*} (unabhängig voneinander)"
echo " Konfiguration:      $ENV_FILE (nur root)"
echo "======================================================================"
echo
echo "Firewall: Ports $BACKUP_PORT und $ARCHIVE_PORT (TCP) nur für den EstrichONE-Server freigeben, z. B."
echo "  sudo ufw allow from <IP des EstrichONE-Servers> to any port $BACKUP_PORT,$ARCHIVE_PORT proto tcp"
[ -n "$BIND" ] && echo "Gebunden an $BIND – erreichbar nur über den Reverse-Proxy (TLS-Durchleitung, siehe docs/backup.md)."
echo
echo "Hinweis: Backup und Archiv auf demselben Rechner ist bequem, aber KEIN zweiter Standort für das Archiv."
echo "Der Kombiserver sollte räumlich getrennt vom EstrichONE-Server stehen (anderes Gebäude, zuhause, Rechenzentrum)."
echo
echo "Aktualisieren: im EstrichONE-Server per Knopfdruck (je Dienst) oder hier denselben Befehl erneut ausführen."
echo "Protokolle: sudo docker logs $B   bzw.   sudo docker logs $A"
exit 0
