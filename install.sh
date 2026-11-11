#!/usr/bin/env bash
# EstrichONE – Installation mit einem Befehl auf einem Linux-Server (Debian/Ubuntu empfohlen):
#
#   curl -fsSL https://raw.githubusercontent.com/florians25/EstrichONE/main/install.sh | sudo bash
#
# Das Skript prüft den Server (Betriebssystem, Docker, Arbeitsspeicher, Speicherplatz, Ports, DNS), installiert bei
# Bedarf Docker, fragt nur Adresse und Firmennamen ab und startet dann den Installer der Suite
# (ghcr.io/florians25/estrichone-installer). Erneut ausführen = Reparatur; Daten und Konfiguration bleiben erhalten.
#
# Befehle (nach „bash -s --“ bzw. direkt als Argument):
#   (ohne)             EstrichONE installieren bzw. reparieren
#   konsole            Hosting-Konsole für viele Instanzen installieren (hosting/install.sh)
#   pruefen            nur prüfen, nichts verändern
#   aktualisieren [V]  auf die neueste bzw. Version V aktualisieren (mit Sicherung, /opt/estrichone/update.sh)
#   wiederherstellen   Neuinstallation aus einer Sicherung (Notfallblatt bereithalten)
#   archiv | backup-ziel | kombi   weiteren Rechner einrichten (Archivserver, Backup-Ziel, beides)
#
# Optionen (alle auch als Umgebungsvariable, für Installationen ohne Rückfragen):
#   --domain D            ESTRICHONE_DOMAIN       Adresse, z. B. estrichone.firma.de
#   --name N              ESTRICHONE_NAME         Firmenname für die Anzeige in den Apps
#   --modus direkt|proxy  ESTRICHONE_MODE         direkt = eigenes HTTPS (Let's Encrypt), proxy = hinter vorhandenem Proxy
#   --version V           ESTRICHONE_VERSION      Version der Suite (Standard: neueste Veröffentlichung)
#   --auto-updates M      ESTRICHONE_AUTO_UPDATE  sicherheit (Standard), patch oder aus
#   --ja                  ESTRICHONE_YES=1        keine Rückfragen, Vorschläge übernehmen
#   --docker-installieren ESTRICHONE_INSTALL_DOCKER=1  Docker ohne Rückfrage installieren (Debian/Ubuntu)
#   --ohne-pruefung       ESTRICHONE_SKIP_CHECKS=1     Mindestanforderungen nur melden, nicht abbrechen
# Alle weiteren ESTRICHONE_*-Variablen (z. B. ESTRICHONE_HTTP_PORT, ESTRICHONE_BIND_ADDRESS, ESTRICHONE_ARCHIVE)
# reicht das Skript an den Installer weiter (siehe deploy/README.md).
#
# Beispiel ohne Rückfragen:
#   curl -fsSL …/install.sh | sudo bash -s -- --ja --domain estrichone.firma.de --name "Estrichbau Muster GmbH"
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112
set -euo pipefail

DIR=/opt/estrichone
REPO="${ESTRICHONE_GITHUB_REPO:-florians25/EstrichONE}"
REF="${ESTRICHONE_REF:-main}"
REGISTRY="${ESTRICHONE_REGISTRY:-ghcr.io/florians25}"
MIN_RAM_MB=1800      # darunter startet die Suite nicht zuverlässig
REC_RAM_MB=3800      # empfohlen: 4 GB
MIN_DISK_GB=8
REC_DISK_GB=30

# --- Ausgabe ------------------------------------------------------------------------------------------------------
if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then
  B=$'\033[1m'; G=$'\033[32m'; Y=$'\033[33m'; R=$'\033[31m'; N=$'\033[0m'
else
  B=""; G=""; Y=""; R=""; N=""
fi
WARNINGS=0
step() { printf '\n%s▸ %s%s\n' "$B" "$*" "$N"; }
ok() { printf '  %s✓%s %s\n' "$G" "$N" "$*"; }
warn() { WARNINGS=$((WARNINGS + 1)); printf '  %s!%s %s\n' "$Y" "$N" "$*"; }
hint() { printf '    %s\n' "$*"; }
fail() { # fail "Meldung" ["Abhilfe" …]
  printf '\n%sFEHLER:%s %s\n' "$R" "$N" "$1" >&2
  shift
  for line in "$@"; do printf '  → %s\n' "$line" >&2; done
  exit 1
}
interactive() { [ "${ESTRICHONE_YES:-0}" != "1" ] && [ -r /dev/tty ] && [ -w /dev/tty ] && { [ -t 0 ] || [ -t 1 ]; } && (: < /dev/tty) 2>/dev/null; }
ask() { # ask VAR "Frage" "Vorschlag"
  local current="${!1:-}"
  if [ -z "$current" ] && interactive; then
    printf '  %s [%s]: ' "$2" "$3" > /dev/tty
    read -r current < /dev/tty || true
  fi
  printf -v "$1" '%s' "${current:-$3}"
}
confirm() { # confirm "Frage" (Standard ja) – ohne Terminal gilt der Standard
  local answer=""
  if interactive; then
    printf '  %s [J/n]: ' "$1" > /dev/tty
    read -r answer < /dev/tty || true
  fi
  case "$answer" in n|N|nein|Nein|no) return 1 ;; *) return 0 ;; esac
}

# --- Argumente ----------------------------------------------------------------------------------------------------
COMMAND=installieren
ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --domain) ESTRICHONE_DOMAIN="${2:?--domain braucht einen Wert}"; shift ;;
    --domain=*) ESTRICHONE_DOMAIN="${1#*=}" ;;
    --name) ESTRICHONE_NAME="${2:?--name braucht einen Wert}"; shift ;;
    --name=*) ESTRICHONE_NAME="${1#*=}" ;;
    --modus|--mode) ESTRICHONE_MODE="${2:?--modus braucht einen Wert}"; shift ;;
    --modus=*|--mode=*) ESTRICHONE_MODE="${1#*=}" ;;
    --version) ESTRICHONE_VERSION="${2:?--version braucht einen Wert}"; shift ;;
    --version=*) ESTRICHONE_VERSION="${1#*=}" ;;
    --auto-updates) ESTRICHONE_AUTO_UPDATE="${2:?--auto-updates braucht einen Wert}"; shift ;;
    --auto-updates=*) ESTRICHONE_AUTO_UPDATE="${1#*=}" ;;
    --ja|--yes|-y) ESTRICHONE_YES=1 ;;
    --docker-installieren|--install-docker) ESTRICHONE_INSTALL_DOCKER=1 ;;
    --ohne-pruefung|--skip-checks) ESTRICHONE_SKIP_CHECKS=1 ;;
    -h|--hilfe|--help) sed -n '2,34p' "${BASH_SOURCE[0]}" 2>/dev/null | sed 's/^# \{0,1\}//' || true; exit 0 ;;
    installieren|install|reparieren|repair) COMMAND=installieren ;;
    konsole|hosting|console) COMMAND=konsole ;;
    pruefen|prüfen|check) COMMAND=pruefen ;;
    aktualisieren|update) COMMAND=aktualisieren ;;
    wiederherstellen|restore) COMMAND=wiederherstellen ;;
    archiv|archive|backup-ziel|backup-target|kombi|kombiserver|backup-archiv) COMMAND=weitere; ARGS+=("$1") ;;
    v[0-9]*|[0-9]*) ARGS+=("${1#v}") ;;
    *) ARGS+=("$1") ;;
  esac
  shift
done
export ESTRICHONE_YES="${ESTRICHONE_YES:-0}"

[ "$(id -u)" = "0" ] || fail "Bitte als root bzw. mit sudo ausführen." \
  "curl -fsSL https://raw.githubusercontent.com/$REPO/$REF/install.sh | sudo bash"

printf '%sEstrichONE – Installation%s\n' "$B" "$N"

# --- Prüfungen ----------------------------------------------------------------------------------------------------
OS_ID=unbekannt; OS_LIKE=""; OS_NAME="Linux"
if [ -r /etc/os-release ]; then
  # shellcheck source=/dev/null
  . /etc/os-release
  OS_ID="${ID:-unbekannt}"; OS_LIKE="${ID_LIKE:-}"; OS_NAME="${PRETTY_NAME:-$OS_ID}"
fi
debian_like() { case " $OS_ID $OS_LIKE " in *" debian "*|*" ubuntu "*) return 0 ;; *) return 1 ;; esac; }
critical() { # Mindestanforderung verfehlt: abbrechen, außer mit --ohne-pruefung
  if [ "$COMMAND" = "pruefen" ]; then warn "$2"; for line in "${@:3}"; do hint "→ $line"; done; return; fi
  if [ "${ESTRICHONE_SKIP_CHECKS:-0}" = "1" ]; then warn "$1 (wegen --ohne-pruefung fortgesetzt)"; else shift; fail "$@"; fi
}

check_system() {
  step "Server prüfen"
  [ "$(uname -s)" = "Linux" ] || fail "EstrichONE läuft auf Linux-Servern." "Ubuntu 24.04 LTS oder Debian 12 verwenden."
  if debian_like; then ok "Betriebssystem: $OS_NAME"; else warn "Betriebssystem: $OS_NAME – getestet sind Debian und Ubuntu; Docker bitte selbst installieren."; fi
  case "$(uname -m)" in
    x86_64|amd64|aarch64|arm64) ok "Prozessor: $(uname -m), $(nproc 2>/dev/null || echo '?') Kerne" ;;
    *) fail "Prozessorarchitektur $(uname -m) wird nicht unterstützt." "Einen Server mit x86_64 (amd64) oder arm64 verwenden." ;;
  esac
  local ram_mb
  ram_mb=$(( $(sed -n 's/^MemTotal: *\([0-9]*\) kB/\1/p' /proc/meminfo 2>/dev/null || echo 0) / 1024 ))
  if [ "$ram_mb" -lt "$MIN_RAM_MB" ]; then
    critical "Arbeitsspeicher: $ram_mb MB" "Zu wenig Arbeitsspeicher: $ram_mb MB (mindestens 2 GB, empfohlen 4 GB)." "Einen größeren Server wählen oder mit --ohne-pruefung trotzdem installieren."
  elif [ "$ram_mb" -lt "$REC_RAM_MB" ]; then
    warn "Arbeitsspeicher: $ram_mb MB – reicht für wenige Benutzer, empfohlen sind 4 GB."
  else
    ok "Arbeitsspeicher: $ram_mb MB"
  fi
  local base="$DIR" free_gb
  while [ ! -d "$base" ]; do base="$(dirname "$base")"; done
  free_gb=$(( $(df -Pk "$base" | awk 'NR==2 {print $4}') / 1024 / 1024 ))
  if [ "$free_gb" -lt "$MIN_DISK_GB" ]; then
    critical "Speicherplatz: $free_gb GB frei" "Zu wenig Speicherplatz unter $base: $free_gb GB frei (mindestens $MIN_DISK_GB GB)." "Datenträger vergrößern oder Platz schaffen (z. B. docker system prune)."
  elif [ "$free_gb" -lt "$REC_DISK_GB" ]; then
    warn "Speicherplatz: $free_gb GB frei – für Fotos, Videos und Sicherungen sind $REC_DISK_GB GB oder mehr empfohlen."
  else
    ok "Speicherplatz: $free_gb GB frei"
  fi
}

install_docker() {
  if ! debian_like; then
    fail "Docker ist nicht installiert." "Docker Engine mit Compose-Plugin installieren: https://docs.docker.com/engine/install/" "Danach diesen Befehl erneut ausführen."
  fi
  if [ "${ESTRICHONE_INSTALL_DOCKER:-}" != "1" ]; then
    if ! interactive; then
      fail "Docker ist nicht installiert." "Mit --docker-installieren (bzw. ESTRICHONE_INSTALL_DOCKER=1) installiert dieses Skript Docker selbst." \
        "Oder manuell: https://docs.docker.com/engine/install/$OS_ID/"
    fi
    confirm "Docker ist nicht installiert. Jetzt aus den offiziellen Docker-Paketquellen installieren?" \
      || fail "Ohne Docker ist keine Installation möglich." "Anleitung: https://docs.docker.com/engine/install/$OS_ID/"
  fi
  printf '  Docker wird installiert (get.docker.com) …\n'
  command -v curl >/dev/null 2>&1 || { apt-get update -qq && apt-get install -y -qq curl ca-certificates >/dev/null; }
  curl -fsSL https://get.docker.com | sh >/tmp/estrichone-docker-install.log 2>&1 \
    || fail "Docker konnte nicht installiert werden (Protokoll: /tmp/estrichone-docker-install.log)." \
      "Internetverbindung und Paketquellen prüfen (apt-get update) und erneut versuchen."
  systemctl enable --now docker >/dev/null 2>&1 || true
}

check_docker() {
  if ! command -v docker >/dev/null 2>&1; then
    install_docker
  fi
  if ! docker info >/dev/null 2>&1; then
    systemctl start docker >/dev/null 2>&1 || service docker start >/dev/null 2>&1 || true
    sleep 2
    docker info >/dev/null 2>&1 || fail "Der Docker-Dienst läuft nicht." "sudo systemctl start docker" "Fehlerursache: sudo journalctl -u docker --no-pager | tail -n 30"
  fi
  if ! docker compose version >/dev/null 2>&1; then
    if debian_like; then
      printf '  Docker-Compose-Plugin wird installiert …\n'
      apt-get update -qq >/dev/null 2>&1 || true
      apt-get install -y -qq docker-compose-plugin >/dev/null 2>&1 || true
    fi
    docker compose version >/dev/null 2>&1 || fail "Das Docker-Compose-Plugin fehlt." "Debian/Ubuntu: sudo apt-get install docker-compose-plugin"
  fi
  ok "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '?'), Compose $(docker compose version --short 2>/dev/null || echo '?')"
}

port_in_use() { # port_in_use PORT → 0, wenn bereits ein Dienst lauscht
  if command -v ss >/dev/null 2>&1; then
    ss -Hltn "sport = :$1" 2>/dev/null | grep -q .
  else
    (: < "/dev/tcp/127.0.0.1/$1") 2>/dev/null
  fi
}
port_owner() { # Name des Prozesses bzw. Containers auf PORT (für verständliche Hinweise)
  local owner=""
  if command -v ss >/dev/null 2>&1; then
    owner="$(ss -Hltnp "sport = :$1" 2>/dev/null | grep -o 'users:(("[^"]*"' | head -n 1 | sed 's/users:(("//; s/"$//')"
  fi
  if [ "$owner" = "docker-proxy" ] && command -v docker >/dev/null 2>&1; then
    owner="Container $(docker ps --filter "publish=$1" --format '{{.Names}}' 2>/dev/null | head -n 1)"
  fi
  printf '%s' "${owner:-unbekannter Dienst}"
}
ours_on_port() { # Port gehört schon zu EstrichONE (Reparatur/Aktualisierung)
  command -v docker >/dev/null 2>&1 && docker ps --filter "publish=$1" --format '{{.Names}}' 2>/dev/null | grep -q '^estrichone'
}

is_ip_or_local() { case "$1" in localhost|*.local|*.lan|*.internal|*.home.arpa) return 0 ;; esac; [[ "$1" =~ ^[0-9.]+$ || "$1" == *:* ]]; }

check_dns() { # check_dns DOMAIN – zeigt die Domain auf diesen Server?
  local domain="$1" resolved addresses
  if is_ip_or_local "$domain"; then
    warn "Adresse $domain: kein öffentlicher Name – Caddy stellt ein eigenes Zertifikat aus (Browserwarnung, Apps brauchen eine echte Domain)."
    return
  fi
  resolved="$(getent ahosts "$domain" 2>/dev/null | awk '{print $1}' | sort -u | tr '\n' ' ' | sed 's/ $//')"
  if [ -z "$resolved" ]; then
    warn "DNS: $domain ist (noch) nicht auflösbar."
    hint "Beim DNS-Anbieter einen A-Eintrag (IPv4) bzw. AAAA-Eintrag (IPv6) für $domain auf diesen Server anlegen."
    hint "Das Zertifikat holt Caddy automatisch, sobald der Eintrag gilt (kann einige Minuten dauern)."
    return
  fi
  addresses=" $(hostname -I 2>/dev/null || true) "
  local match=0 ip
  for ip in $resolved; do case "$addresses" in *" $ip "*) match=1 ;; esac; done
  if [ "$match" = "1" ]; then
    ok "DNS: $domain → $resolved (dieser Server)"
  else
    warn "DNS: $domain → $resolved – keine dieser Adressen gehört zu diesem Server."
    hint "Hinter einem Router (NAT) ist das normal: dort 80 und 443 auf diesen Server weiterleiten."
    hint "Sonst den DNS-Eintrag korrigieren, damit Let's Encrypt das Zertifikat ausstellen kann."
  fi
}

check_firewall() { # check_firewall PORT…
  if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q '^Status: active'; then
    local port missing=""
    for port in "$@"; do ufw status 2>/dev/null | grep -Eq "^$port(/tcp)?[[:space:]]+ALLOW" || missing="$missing $port"; done
    if [ -n "$missing" ]; then
      warn "Firewall (ufw) ist aktiv und lässt Port(s)${missing} nicht zu."
      for port in $missing; do hint "Freigeben: sudo ufw allow $port/tcp"; done
    else
      ok "Firewall (ufw): Port(s) $* freigegeben"
    fi
  fi
}

latest_version() { # neueste veröffentlichte Version (GitHub-Releases); leer, wenn nicht ermittelbar
  curl -fsSL --max-time 10 "https://api.github.com/repos/$REPO/releases/latest" 2>/dev/null \
    | sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p' | head -n 1
}

# Vorhandener Caddy auf dem Server (Paket, systemd): EstrichONE als eigene Site eintragen, Caddy holt das Zertifikat.
CADDYFILE=/etc/caddy/Caddyfile
host_caddy() { [ -f "$CADDYFILE" ] && command -v caddy >/dev/null 2>&1 && systemctl is-active --quiet caddy 2>/dev/null; }
configure_host_caddy() { # configure_host_caddy DOMAIN PORT
  local begin="# >>> EstrichONE ($1) – von install.sh verwaltet" end="# <<< EstrichONE ($1)"
  local tmp
  tmp="$(mktemp)"
  awk -v b="$begin" -v e="$end" '$0==b {skip=1} !skip {print} $0==e {skip=0}' "$CADDYFILE" > "$tmp"
  cat >> "$tmp" <<EOF
$begin
$1 {
	encode zstd gzip
	request_body {
		max_size 600MB
	}
	reverse_proxy 127.0.0.1:$2
}
$end
EOF
  if caddy validate --config "$tmp" --adapter caddyfile >/dev/null 2>&1; then
    cp "$CADDYFILE" "$CADDYFILE.vor-estrichone"
    cat "$tmp" > "$CADDYFILE"
    if systemctl reload caddy; then
      ok "Vorhandener Caddy: Site $1 → 127.0.0.1:$2 eingetragen (Zertifikat holt Caddy automatisch)."
    else
      warn "Caddy konnte nicht neu geladen werden: sudo systemctl status caddy"
    fi
  else
    warn "Caddyfile mit EstrichONE-Eintrag ist ungültig – nichts geändert. Eintrag bitte selbst ergänzen:"
    hint "$1 { reverse_proxy 127.0.0.1:$2 }"
  fi
  rm -f "$tmp"
}

# Hosting-Konsole: eigenes Installationsskript (gleiche Prüfungen für Docker, RAM, Speicher, Ports).
run_console_installer() {
  local script="" here
  # Aus einem Checkout gestartet (bash install.sh konsole): dortiges Skript; über curl | bash: von GitHub laden.
  if [ -f "${BASH_SOURCE[0]:-}" ]; then here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; else here=""; fi
  if [ -n "$here" ] && [ -f "$here/hosting/install.sh" ]; then
    script="$here/hosting/install.sh"
  else
    script="$(mktemp)"
    curl -fsSL "https://raw.githubusercontent.com/$REPO/$REF/hosting/install.sh" -o "$script" \
      || fail "Installationsskript der Konsole nicht ladbar." "Internetverbindung prüfen; Adresse: https://raw.githubusercontent.com/$REPO/$REF/hosting/install.sh"
  fi
  export HOSTING_INSTALL_DOCKER=1 HOSTING_NONINTERACTIVE="${HOSTING_NONINTERACTIVE:-$ESTRICHONE_YES}"
  exec bash "$script" "${ARGS[@]+"${ARGS[@]}"}"
}

# --- Ablauf -------------------------------------------------------------------------------------------------------
case "$COMMAND" in
  aktualisieren)
    [ -x "$DIR/update.sh" ] || fail "Keine Installation in $DIR gefunden." "Erst installieren: curl -fsSL https://raw.githubusercontent.com/$REPO/$REF/install.sh | sudo bash"
    exec "$DIR/update.sh" "${ARGS[@]+"${ARGS[@]}"}" ;;
  konsole)
    check_system
    check_docker
    for port in 80 443; do
      if port_in_use "$port" && ! docker ps --filter "publish=$port" --format '{{.Names}}' | grep -q '^estrichone-hosting'; then
        warn "Port $port ist belegt ($(port_owner "$port")) – die Konsole braucht 80/443 für eigenes HTTPS."
        hint "Hinter einem vorhandenen Proxy installieren: HOSTING_MODE=proxy (siehe hosting/README.md)."
      fi
    done
    run_console_installer ;;
esac

check_system
EXISTING=0
[ -f "$DIR/.env" ] && EXISTING=1
env_value() { sed -n "s/^$1=//p" "$DIR/.env" 2>/dev/null | tail -n 1 | sed 's/^"\(.*\)"$/\1/'; }

if [ "$COMMAND" = "pruefen" ]; then
  if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    ok "Docker $(docker version --format '{{.Server.Version}}' 2>/dev/null)"
    docker compose version >/dev/null 2>&1 || warn "Docker-Compose-Plugin fehlt (sudo apt-get install docker-compose-plugin)."
  elif command -v docker >/dev/null 2>&1; then
    warn "Docker ist installiert, der Dienst läuft aber nicht (sudo systemctl start docker)."
  else
    warn "Docker ist nicht installiert – die Installation bietet das an (Debian/Ubuntu)."
  fi
  for port in 80 443 8181 8143; do
    if port_in_use "$port"; then
      if ours_on_port "$port"; then ok "Port $port: EstrichONE"; else warn "Port $port belegt: $(port_owner "$port")"; fi
    else
      ok "Port $port frei"
    fi
  done
  [ -n "${ESTRICHONE_DOMAIN:-}" ] && check_dns "$ESTRICHONE_DOMAIN"
  [ "$EXISTING" = "1" ] && ok "Vorhandene Installation: $DIR (Version $(env_value ESTRICHONE_VERSION))"
  printf '\nPrüfung beendet: %s Hinweis(e). Es wurde nichts verändert.\n' "$WARNINGS"
  exit 0
fi

check_docker

# Zielversion: bei vorhandener Installation die installierte (Reparatur), sonst die neueste Veröffentlichung.
VERSION="${ESTRICHONE_VERSION:-}"
[ -n "$VERSION" ] || { [ "$EXISTING" = "1" ] && [ "$COMMAND" = "installieren" ] && VERSION="$(env_value ESTRICHONE_VERSION)"; } || true
[ -n "$VERSION" ] || VERSION="$(latest_version)"
[ -n "$VERSION" ] || { VERSION=latest; warn "Neueste Version nicht ermittelbar (GitHub nicht erreichbar) – verwende „latest“."; }
VERSION="${VERSION#v}"
[[ "$VERSION" =~ ^(latest|[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?)$ ]] || fail "Ungültige Version: $VERSION" "Beispiel: --version 1.0.13"
IMAGE="$REGISTRY/estrichone-installer:$VERSION"

HOST_CADDY_SITE=""
if [ "$COMMAND" = "installieren" ] && [ "$EXISTING" = "0" ]; then
  step "Angaben"
  default_domain="$(hostname -f 2>/dev/null || hostname)"
  case "$default_domain" in *.*) ;; *) default_domain="$(hostname -I 2>/dev/null | awk '{print $1}')" ;; esac
  ask ESTRICHONE_DOMAIN "Adresse von EstrichONE (Domain ohne https://, z. B. estrichone.firma.de)" "${default_domain:-localhost}"
  ESTRICHONE_DOMAIN="${ESTRICHONE_DOMAIN#http://}"; ESTRICHONE_DOMAIN="${ESTRICHONE_DOMAIN#https://}"; ESTRICHONE_DOMAIN="${ESTRICHONE_DOMAIN%%/*}"
  [[ "$ESTRICHONE_DOMAIN" =~ ^[A-Za-z0-9.-]+$ ]] || fail "Ungültige Adresse: $ESTRICHONE_DOMAIN" "Nur den Namen angeben, z. B. estrichone.firma.de"
  ask ESTRICHONE_NAME "Firmenname (Anzeige in Web und Apps)" "EstrichONE"
  case "$ESTRICHONE_NAME" in *'"'*) fail "Der Firmenname darf keine Anführungszeichen enthalten." ;; esac

  # Betriebsart: 80/443 frei → EstrichONE macht HTTPS selbst (Let's Encrypt). Belegt → hinter dem vorhandenen Proxy.
  if [ -z "${ESTRICHONE_MODE:-}" ]; then
    if port_in_use 80 || port_in_use 443; then
      ESTRICHONE_MODE=proxy
      ok "Ports 80/443 sind belegt ($(port_owner 443)) – EstrichONE läuft hinter diesem Proxy (Betriebsart proxy)."
    else
      ESTRICHONE_MODE=direkt
      ok "Ports 80/443 sind frei – EstrichONE holt das Zertifikat selbst (Betriebsart direkt, Let's Encrypt)."
    fi
  fi
  case "$ESTRICHONE_MODE" in direkt|proxy) ;; *) fail "Betriebsart muss „direkt“ oder „proxy“ sein." ;; esac
  if [ "$ESTRICHONE_MODE" = "direkt" ]; then
    : "${ESTRICHONE_HTTP_PORT:=80}" "${ESTRICHONE_HTTPS_PORT:=443}"
    check_ports=("$ESTRICHONE_HTTP_PORT" "$ESTRICHONE_HTTPS_PORT")
  else
    : "${ESTRICHONE_HTTP_PORT:=8181}"
    check_ports=("$ESTRICHONE_HTTP_PORT")
    if host_caddy && ! is_ip_or_local "$ESTRICHONE_DOMAIN"; then
      if confirm "Auf diesem Server läuft Caddy. EstrichONE dort als Site $ESTRICHONE_DOMAIN eintragen (HTTPS automatisch)?"; then
        HOST_CADDY_SITE="$ESTRICHONE_DOMAIN"
        : "${ESTRICHONE_BIND_ADDRESS:=127.0.0.1}"
      fi
    fi
  fi
  export ESTRICHONE_MODE ESTRICHONE_DOMAIN ESTRICHONE_NAME ESTRICHONE_HTTP_PORT
  [ -n "${ESTRICHONE_HTTPS_PORT:-}" ] && export ESTRICHONE_HTTPS_PORT
  [ -n "${ESTRICHONE_BIND_ADDRESS:-}" ] && export ESTRICHONE_BIND_ADDRESS

  step "Netz prüfen"
  for port in "${check_ports[@]}"; do
    if port_in_use "$port"; then
      fail "Port $port ist bereits belegt ($(port_owner "$port"))." \
        "Den Dienst beenden oder andere Ports wählen, z. B. --modus proxy bzw. ESTRICHONE_HTTP_PORT=8181." \
        "Belegte Ports anzeigen: sudo ss -ltnp"
    fi
    ok "Port $port frei"
  done
  check_dns "$ESTRICHONE_DOMAIN"
  [ "$ESTRICHONE_MODE" = "direkt" ] && check_firewall "${check_ports[@]}"
  ask ESTRICHONE_AUTO_UPDATE "Sicherheitsupdates automatisch nachts einspielen? (sicherheit/patch/aus)" "sicherheit"
elif [ "$EXISTING" = "1" ]; then
  step "Vorhandene Installation"
  ok "$DIR – Version $(env_value ESTRICHONE_VERSION), Adresse $(env_value ESTRICHONE_DOMAIN) – wird repariert (Daten und .env bleiben)."
  [ "$COMMAND" = "installieren" ] && [ "$VERSION" != "$(env_value ESTRICHONE_VERSION)" ] \
    && hint "Zielversion $VERSION. Für Aktualisierungen mit Sicherung besser: sudo /opt/estrichone/update.sh"
fi

case "${ESTRICHONE_AUTO_UPDATE:-}" in ""|sicherheit|patch|aus) ;; *) fail "--auto-updates: sicherheit, patch oder aus" ;; esac

step "Installer $IMAGE"
mkdir -p "$DIR"
if ! docker pull -q "$IMAGE" >/dev/null 2>&1; then
  docker image inspect "$IMAGE" >/dev/null 2>&1 \
    || fail "Installer-Image $IMAGE nicht ladbar." "Internetverbindung und Version prüfen (Liste: https://github.com/$REPO/releases)." \
      "Firmennetz mit Proxy: Docker-Proxy einrichten (https://docs.docker.com/engine/daemon/proxy/)."
  warn "Laden fehlgeschlagen – vorhandenes lokales Image wird verwendet."
fi
docker_args=(--rm -v /var/run/docker.sock:/var/run/docker.sock -v "$DIR:$DIR")
while IFS= read -r name; do
  case "$name" in
    ESTRICHONE_YES|ESTRICHONE_INSTALL_DOCKER|ESTRICHONE_SKIP_CHECKS|ESTRICHONE_AUTO_UPDATE|ESTRICHONE_VERSION|ESTRICHONE_REF|ESTRICHONE_GITHUB_REPO) continue ;;
  esac
  docker_args+=(-e "$name")
done < <(env | grep -oE '^ESTRICHONE_[A-Z0-9_]+=' | tr -d '=' | sort -u || true)
installer_args=()
case "$COMMAND" in
  wiederherstellen) installer_args=(restore) ;;
  weitere) installer_args=("${ARGS[@]}") ;;
esac
if interactive; then
  docker run -it "${docker_args[@]}" "$IMAGE" "${installer_args[@]+"${installer_args[@]}"}" < /dev/tty
else
  docker run -i "${docker_args[@]}" "$IMAGE" "${installer_args[@]+"${installer_args[@]}"}" < /dev/null
fi

[ "$COMMAND" = "weitere" ] && exit 0

if [ -n "$HOST_CADDY_SITE" ]; then
  configure_host_caddy "$HOST_CADDY_SITE" "$(env_value ESTRICHONE_HTTP_PORT)"
fi

if [ -n "${ESTRICHONE_AUTO_UPDATE:-}" ] && [ -x "$DIR/update.sh" ]; then
  if [ "$ESTRICHONE_AUTO_UPDATE" = "aus" ]; then
    "$DIR/update.sh" --automatik aus >/dev/null 2>&1 || true
  elif "$DIR/update.sh" --automatik "$ESTRICHONE_AUTO_UPDATE" >/dev/null 2>&1; then
    ok "Automatische Updates: $ESTRICHONE_AUTO_UPDATE (nachts 02:00–05:00, mit Sicherung vorher)"
  else
    warn "Automatische Updates nicht eingerichtet – später: sudo /opt/estrichone/update.sh --automatik sicherheit"
  fi
fi

printf '\n%sFertig.%s Verwalten: cd %s && sudo docker compose -p estrichone ps · Aktualisieren: sudo %s/update.sh\n' "$G" "$N" "$DIR" "$DIR"
printf 'Erneut ausführen repariert die Installation; „… | sudo bash -s -- pruefen“ prüft nur.\n'
[ "$WARNINGS" -gt 0 ] && printf '%s%s Hinweis(e) oben beachten.%s\n' "$Y" "$WARNINGS" "$N"
exit 0
