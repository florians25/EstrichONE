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
#   ESTRICHONE_OFFLOAD_DATA_DIR=/srv/auslagerung   zusätzlich die Speicher-Auslagerung (Storage für ausgelagerte Originale,
#                                               Container estrichone-offload, Port 8445) mit eigenem Ordner einrichten –
#                                               oder -e ESTRICHONE_OFFLOAD=an (Docker-Volume). Einmal gesetzt, wird es gemerkt.
#   ESTRICHONE_OFFLOAD_PORT=8445, ESTRICHONE_OFFLOAD_TRASH_DAYS=30   Port bzw. Papierkorb-Frist der Auslagerung
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
O=estrichone-offload
echo "EstrichONE-Kombiserver $VERSION (Backup-Ziel + Archivserver) wird eingerichtet …"
exists "$B" && echo "Vorhandenes Backup-Ziel gefunden – Daten, Kopplung, Port und Ordner bleiben erhalten."
exists "$A" && echo "Vorhandener Archivserver gefunden – Daten, Kopplung, Port und Ordner bleiben erhalten."
exists "$O" && echo "Vorhandene Speicher-Auslagerung gefunden – Daten, Kopplung, Port und Ordner bleiben erhalten."

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
# Speicher-Auslagerung (optional, dritter Dienst): an, sobald ein Ordner bzw. ESTRICHONE_OFFLOAD=an angegeben wurde, sie schon
# läuft oder in der .env gemerkt ist. „aus“ richtet sie nicht ein (ein vorhandener Container bleibt unverändert).
OFFLOAD_DATA="$(pick ESTRICHONE_OFFLOAD_DATA_DIR "$(container_data "$O")" "")"
OFFLOAD_DEFAULT=aus
if [ -n "$OFFLOAD_DATA" ] || exists "$O"; then OFFLOAD_DEFAULT=an; fi
OFFLOAD="$(pick ESTRICHONE_OFFLOAD "" "$OFFLOAD_DEFAULT")"
case "$OFFLOAD" in an|on|1|ja|true) OFFLOAD=an ;; aus|off|0|nein|false|"") OFFLOAD=aus ;; *) fail "ESTRICHONE_OFFLOAD bitte an oder aus." ;; esac
[ -n "${ESTRICHONE_OFFLOAD_DATA_DIR:-}" ] && [ "${ESTRICHONE_OFFLOAD:-}" = "" ] && OFFLOAD=an
OFFLOAD_PORT="$(pick ESTRICHONE_OFFLOAD_PORT "$(container_port "$O")" 8445)"
TRASH_DAYS="$(pick ESTRICHONE_OFFLOAD_TRASH_DAYS "$(container_env "$O" ESTRICHONE_OFFLOAD_TRASH_DAYS)" "")"

for port in "$BACKUP_PORT" "$ARCHIVE_PORT" "$OFFLOAD_PORT"; do
  case "$port" in ''|*[!0-9]*) fail "Ungültiger Port: $port" ;; esac
  if [ "$port" -lt 1 ] || [ "$port" -gt 65535 ]; then fail "Ungültiger Port: $port"; fi
done
[ "$BACKUP_PORT" != "$ARCHIVE_PORT" ] || fail "Backup-Ziel und Archiv brauchen verschiedene Ports (Standard 8444 und 8443)."
if [ "$OFFLOAD" = an ] && { [ "$OFFLOAD_PORT" = "$BACKUP_PORT" ] || [ "$OFFLOAD_PORT" = "$ARCHIVE_PORT" ]; }; then
  fail "Die Speicher-Auslagerung braucht einen eigenen Port (Standard 8445, Backup-Ziel 8444, Archiv 8443)."
fi
case "$TRASH_DAYS" in ''|*[!0-9]*) [ -z "$TRASH_DAYS" ] || fail "ESTRICHONE_OFFLOAD_TRASH_DAYS bitte als Zahl (Tage)." ;; esac
case "$NAME" in *'"'*|*'$'*|*'`'*|*"\\"*) fail "Der Name darf keine Anführungszeichen, \$, \` oder \\ enthalten." ;; esac
for dir in "$BACKUP_DATA" "$ARCHIVE_DATA" "$OFFLOAD_DATA"; do
  case "$dir" in ""|/*) ;; *) fail "Datenordner bitte als absoluten Pfad angeben: $dir" ;; esac
  case "$dir" in "$DIR"|"$DIR"/*) fail "Datenordner nicht in $DIR (dort liegt nur die Konfiguration): $dir" ;; esac
done
# Keine Vermischung von Backup-, Archiv- und Auslagerungsdaten: getrennte, nicht ineinander liegende Ordner.
if [ -n "$BACKUP_DATA" ] && [ -n "$ARCHIVE_DATA" ]; then
  b="${BACKUP_DATA%/}/"; a="${ARCHIVE_DATA%/}/"
  case "$a" in "$b"*) fail "Archivordner liegt im Backup-Ordner – bitte getrennte Ordner verwenden." ;; esac
  case "$b" in "$a"*) fail "Backup-Ordner liegt im Archivordner – bitte getrennte Ordner verwenden." ;; esac
fi
if [ -n "$OFFLOAD_DATA" ]; then
  o="${OFFLOAD_DATA%/}/"
  for other in "$BACKUP_DATA" "$ARCHIVE_DATA"; do
    [ -n "$other" ] || continue
    x="${other%/}/"
    case "$o" in "$x"*) fail "Auslagerungsordner liegt im Backup- bzw. Archivordner – bitte getrennte Ordner verwenden." ;; esac
    case "$x" in "$o"*) fail "Backup- bzw. Archivordner liegt im Auslagerungsordner – bitte getrennte Ordner verwenden." ;; esac
  done
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
set_env ESTRICHONE_OFFLOAD "$OFFLOAD"
set_env ESTRICHONE_OFFLOAD_PORT "$OFFLOAD_PORT"
set_env ESTRICHONE_OFFLOAD_DATA_DIR "$OFFLOAD_DATA"
set_env ESTRICHONE_OFFLOAD_TRASH_DAYS "$TRASH_DAYS"
chmod 600 "$ENV_FILE"

# Beide Dienste nacheinander mit dem bewährten Einzel-Installer (gleiche Container, Volumes, Updater, Rücksprung).
# Jeder Dienst bekommt nur seine eigenen Werte – der Updater gibt sie bei Fernaktualisierungen unverändert weiter.
run_role() { # run_role <rolle> <log> VAR=WERT …
  role="$1"; log="$2"; shift 2
  if ! env -u ESTRICHONE_PORT -u ESTRICHONE_DATA_DIR -u ESTRICHONE_PULL_FROM -u ESTRICHONE_PULL_CODE -u ESTRICHONE_PULL_FINGERPRINT \
      -u ESTRICHONE_PULL_INTERVAL_MINUTES -u ESTRICHONE_MIN_RETENTION_YEARS -u ESTRICHONE_NEW_CODE -u ESTRICHONE_NAME \
      -u ESTRICHONE_ADDRESS -u ESTRICHONE_BIND_ADDRESS -u ESTRICHONE_REMOTE_UPDATE -u ESTRICHONE_OFFLOAD_TRASH_DAYS "$@" estrichone-peer "$role" > "$log" 2>&1; then
    cat "$log" >&2
    fail "Einrichtung „$role“ fehlgeschlagen (Meldung oben). Der andere Dienst ist unverändert."
  fi
}
LOGS="$(mktemp -d)"
STEPS=2; [ "$OFFLOAD" = an ] && STEPS=3
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
echo "1/$STEPS Backup-Ziel (Port $BACKUP_PORT) …"
run_role backup-ziel "$LOGS/backup.log" "$@"

set -- ESTRICHONE_PORT="$ARCHIVE_PORT" ESTRICHONE_NAME="$NAME – Archiv" ESTRICHONE_REMOTE_UPDATE="$REMOTE"
[ -n "$ADDRESS" ] && set -- "$@" ESTRICHONE_ADDRESS="$ADDRESS"
[ -n "$BIND" ] && set -- "$@" ESTRICHONE_BIND_ADDRESS="$BIND"
[ -n "$ARCHIVE_DATA" ] && set -- "$@" ESTRICHONE_DATA_DIR="$ARCHIVE_DATA"
[ -n "$RETENTION" ] && set -- "$@" ESTRICHONE_MIN_RETENTION_YEARS="$RETENTION"
echo "2/$STEPS Archivserver (Port $ARCHIVE_PORT) …"
run_role archiv "$LOGS/archive.log" "$@"

if [ "$OFFLOAD" = an ]; then
  set -- ESTRICHONE_PORT="$OFFLOAD_PORT" ESTRICHONE_NAME="$NAME – Storage" ESTRICHONE_REMOTE_UPDATE="$REMOTE"
  [ -n "$ADDRESS" ] && set -- "$@" ESTRICHONE_ADDRESS="$ADDRESS"
  [ -n "$BIND" ] && set -- "$@" ESTRICHONE_BIND_ADDRESS="$BIND"
  [ -n "$OFFLOAD_DATA" ] && set -- "$@" ESTRICHONE_DATA_DIR="$OFFLOAD_DATA"
  [ -n "$TRASH_DAYS" ] && set -- "$@" ESTRICHONE_OFFLOAD_TRASH_DAYS="$TRASH_DAYS"
  echo "3/3 Speicher-Auslagerung (Storage, Port $OFFLOAD_PORT) …"
  run_role auslagerung "$LOGS/offload.log" "$@"
fi

value() { sed -n "s/.*$2: *//p" "$1" | head -n 1 | sed 's/ *(.*//; s/ *$//'; }
SHOWN_ADDRESS="$(sed -n 's/.*Adresse: *\([^:]*\):.*/\1/p' "$LOGS/backup.log" | head -n 1)"
BACKUP_FP="$(value "$LOGS/backup.log" Fingerabdruck)"
ARCHIVE_FP="$(value "$LOGS/archive.log" Fingerabdruck)"
B_OPEN="$(value "$LOGS/backup.log" Kopplungscode)"
A_OPEN="$(value "$LOGS/archive.log" Kopplungscode)"
B_REMOTE="$(value "$LOGS/backup.log" Fernaktualisierung)"
A_REMOTE="$(value "$LOGS/archive.log" Fernaktualisierung)"
O_OPEN=""; OFFLOAD_FP=""; O_REMOTE=""
if [ "$OFFLOAD" = an ]; then
  O_OPEN="$(value "$LOGS/offload.log" Kopplungscode)"
  OFFLOAD_FP="$(value "$LOGS/offload.log" Fingerabdruck)"
  O_REMOTE="$(value "$LOGS/offload.log" Fernaktualisierung)"
fi
rm -rf "$LOGS"

# EIN Code für beide Dienste: jeder Dienst, der noch auf eine Kopplung wartet (oder mit ESTRICHONE_NEW_CODE=1),
# bekommt denselben Code. Zieht das Backup-Ziel (Topologie A), ist es der Code aus dem Backup Center.
NEED_B=0; NEED_A=0; NEED_O=0
if [ -z "$PULL_FROM" ] && { [ -n "$B_OPEN" ] || [ "${ESTRICHONE_NEW_CODE:-}" = "1" ]; }; then NEED_B=1; fi
if [ -n "$A_OPEN" ] || [ "${ESTRICHONE_NEW_CODE:-}" = "1" ]; then NEED_A=1; fi
if [ "$OFFLOAD" = an ] && { [ -n "$O_OPEN" ] || [ "${ESTRICHONE_NEW_CODE:-}" = "1" ]; }; then NEED_O=1; fi
CODE=""
if [ "$NEED_B" = 1 ] || [ "$NEED_A" = 1 ] || [ "$NEED_O" = 1 ]; then
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
  [ "$NEED_O" = 1 ] && services="$services $O"
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
[ "$OFFLOAD" = an ] && echo "   Storage (Auslagerung): $HOST:$OFFLOAD_PORT$( [ -n "$OFFLOAD_DATA" ] && echo "   (Ordner $OFFLOAD_DATA)" )"
if [ -n "$CODE" ]; then
  echo "   Kopplungscode:      $CODE        (24 Stunden gültig, einmal je Dienst)"
  echo
  if [ -n "$PULL_FROM" ]; then
    echo " In EstrichONE: System → Backup-Ziele → „Kombiserver koppeln“ → „Kombiserver zieht“ → Schritt 2: Adresse des"
    echo " Archivs eingeben (Port $ARCHIVE_PORT am Router weiterleiten oder Tunnel-Adresse) – der Code ist schon bekannt."
  else
    echo " In EstrichONE: System → Backup-Ziele → „Kombiserver koppeln“ – Adresse und Code eingeben (koppelt beide Dienste)."
  fi
  if [ "$NEED_O" = 1 ]; then
    echo " Speicher-Auslagerung: System → Speicher-Auslagerung → „Storage koppeln“ – Adresse $HOST:$OFFLOAD_PORT und derselbe Code."
  fi
else
  echo
  if [ "$OFFLOAD" = an ]; then
    echo " Alle Dienste sind bereits gekoppelt. Neuer Code für alle: denselben Befehl mit -e ESTRICHONE_NEW_CODE=1."
  else
    echo " Beide Dienste sind bereits gekoppelt. Neuer Code für beide: denselben Befehl mit -e ESTRICHONE_NEW_CODE=1."
  fi
fi
echo " Fingerabdruck Backup-Ziel: $BACKUP_FP"
echo " Fingerabdruck Archiv:      $ARCHIVE_FP"
[ "$OFFLOAD" = an ] && echo " Fingerabdruck Storage:     $OFFLOAD_FP"
echo "                    (zeigt EstrichONE nach dem Koppeln ebenfalls an – müssen übereinstimmen)"
echo " Fernaktualisierung: Backup-Ziel ${B_REMOTE%% –*}, Archiv ${A_REMOTE%% –*}$( [ "$OFFLOAD" = an ] && echo ", Storage ${O_REMOTE%% –*}" ) (unabhängig voneinander)"
echo " Konfiguration:      $ENV_FILE (nur root)"
echo "======================================================================"
echo
PORTS="$BACKUP_PORT,$ARCHIVE_PORT"; [ "$OFFLOAD" = an ] && PORTS="$PORTS,$OFFLOAD_PORT"
echo "Firewall: Ports $PORTS (TCP) nur für den EstrichONE-Server freigeben, z. B."
echo "  sudo ufw allow from <IP des EstrichONE-Servers> to any port $PORTS proto tcp"
[ -n "$BIND" ] && echo "Gebunden an $BIND – erreichbar nur über den Reverse-Proxy (TLS-Durchleitung, siehe docs/backup.md)."
echo
echo "Hinweis: Backup und Archiv auf demselben Rechner ist bequem, aber KEIN zweiter Standort für das Archiv."
echo "Der Kombiserver sollte räumlich getrennt vom EstrichONE-Server stehen (anderes Gebäude, zuhause, Rechenzentrum)."
echo
echo "Aktualisieren: im EstrichONE-Server per Knopfdruck (je Dienst) oder hier denselben Befehl erneut ausführen."
if [ "$OFFLOAD" = an ]; then
  echo "Protokolle: sudo docker logs $B   bzw.   sudo docker logs $A   bzw.   sudo docker logs $O"
else
  echo "Protokolle: sudo docker logs $B   bzw.   sudo docker logs $A"
  echo
  echo "Speicher-Auslagerung (optional, wenn der EstrichONE-Server knapp wird): denselben Befehl zusätzlich mit"
  echo "  -e ESTRICHONE_OFFLOAD_DATA_DIR=/pfad/auslagerung   (eigener Ordner, z. B. auf der großen Platte) oder -e ESTRICHONE_OFFLOAD=an"
fi
exit 0
