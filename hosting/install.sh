#!/usr/bin/env bash
# EstrichONE Hosting-Konsole – Installation mit einem Befehl:
#
#   curl -fsSL https://raw.githubusercontent.com/florians25/EstrichONE/main/hosting/install.sh | sudo bash
#
# Danach steht auf dem Server der Befehl „estrichone-hosting“ bereit:
#   sudo estrichone-hosting                 Installation/Reparatur (erneut ausführen ist jederzeit gefahrlos)
#   sudo estrichone-hosting update [VER]    Konsole auf VER (Standard: latest) aktualisieren
#   sudo estrichone-hosting status          Zustand von Konsole und Proxy
#   sudo estrichone-hosting logs            Protokoll der Konsole (Strg+C beendet)
#   sudo estrichone-hosting reset-admin [BENUTZER]   Notfall: neues Startpasswort, Zwei-Faktor aus
#   sudo estrichone-hosting uninstall [--purge]      Konsole entfernen (--purge: auch ALLE Instanzen und Daten)
#
# Ohne Rückfragen (Automatisierung): Werte als Umgebungsvariablen, z. B.
#   curl -fsSL …/install.sh | sudo HOSTING_BASE_DOMAIN=estrichone-hosting.de HOSTING_ACME_EMAIL=admin@firma.de bash
# Weitere: HOSTING_MODE=direkt|proxy, HOSTING_CONSOLE_DOMAIN, HOSTING_ROOT, HOSTING_ADMIN_USER, HOSTING_ADMIN_EMAIL,
#          HOSTING_ADMIN_PASSWORD, HOSTING_TLS=acme|internal|off, HOSTING_HTTP_PORT, HOSTING_HTTPS_PORT,
#          HOSTING_VERSION (Image-Tag), HOSTING_IMAGE (ganzes Image), HOSTING_PULL=never (nur lokales Image),
#          HOSTING_INSTALL_DOCKER=1 (Docker ohne Rückfrage installieren), HOSTING_ALLOWED_NETWORKS.
# Deutsche Anführungszeichen („…“) in Meldungen sind beabsichtigt.
# shellcheck disable=SC1111,SC1112
set -euo pipefail

CONF=/etc/estrichone-hosting.conf
CLI=/usr/local/bin/estrichone-hosting
REGISTRY="${ESTRICHONE_REGISTRY:-ghcr.io/florians25}"

say() { printf '%s\n' "$*"; }
fail() { printf 'FEHLER: %s\n' "$*" >&2; exit 1; }
interactive() { [ -r /dev/tty ] && [ -w /dev/tty ] && [ "${HOSTING_NONINTERACTIVE:-0}" != "1" ] && { [ -t 0 ] || [ -t 1 ]; }; }
ask() { # ask VAR "Frage" "Standard"
  local current="${!1:-}"
  if [ -z "$current" ] && interactive; then
    printf '%s [%s]: ' "$2" "$3" > /dev/tty
    read -r current < /dev/tty || true
  fi
  printf -v "$1" '%s' "${current:-$3}"
}

[ "$(id -u)" = "0" ] || fail "Bitte mit sudo bzw. als root ausführen."

COMMAND="${1:-install}"
[ $# -gt 0 ] && shift
case "$COMMAND" in
  install|repair|update|status|logs|reset-admin|uninstall) ;;
  -h|--help|help) sed -n '2,20p' "${BASH_SOURCE[0]:-$CLI}" 2>/dev/null | sed 's/^# \{0,1\}//'; exit 0 ;;
  *) fail "Unbekannter Befehl „$COMMAND“ (install, update, status, logs, reset-admin, uninstall)." ;;
esac

# Gespeicherte Einstellungen früherer Läufe (Datenverzeichnis, Image)
if [ -f "$CONF" ]; then
  # shellcheck source=/dev/null
  . "$CONF"
fi

# --- Docker -----------------------------------------------------------------------------------------------------
if ! command -v docker >/dev/null 2>&1; then
  [ "$COMMAND" = "install" ] || [ "$COMMAND" = "repair" ] || fail "Docker ist nicht installiert."
  answer="${HOSTING_INSTALL_DOCKER:-}"
  if [ -z "$answer" ] && interactive; then
    printf 'Docker ist nicht installiert. Jetzt über get.docker.com installieren? [J/n]: ' > /dev/tty
    read -r answer < /dev/tty || true
    case "$answer" in n|N|nein|Nein) answer=0 ;; *) answer=1 ;; esac
  fi
  [ "$answer" = "1" ] || fail "Docker fehlt. Installation: https://docs.docker.com/engine/install/ (oder HOSTING_INSTALL_DOCKER=1)."
  say "Docker wird installiert …"
  curl -fsSL https://get.docker.com | sh
  systemctl enable --now docker >/dev/null 2>&1 || true
fi
docker info >/dev/null 2>&1 || fail "Der Docker-Dienst läuft nicht (systemctl start docker)."
docker compose version >/dev/null 2>&1 || fail "Das Docker-Compose-Plugin fehlt (Paket docker-compose-plugin)."

pull_image() {
  if [ "${HOSTING_PULL:-always}" = "never" ]; then
    docker image inspect "$IMAGE" >/dev/null 2>&1 || fail "Image $IMAGE ist lokal nicht vorhanden."
    return
  fi
  say "Image $IMAGE laden …"
  if ! docker pull -q "$IMAGE" >/dev/null; then
    docker image inspect "$IMAGE" >/dev/null 2>&1 || fail "Image $IMAGE nicht ladbar (Netz/Registry prüfen)."
    say "Hinweis: Laden fehlgeschlagen – vorhandenes lokales Image wird verwendet."
  fi
}

# Container mit Docker-Socket und Datenverzeichnis (gleicher Pfad innen und außen) starten.
run_image() { # run_image BEFEHL …
  local args=(--rm -v /var/run/docker.sock:/var/run/docker.sock -v "$ROOT:$ROOT" -e HOSTING_ROOT="$ROOT" -e HOSTING_IMAGE="$IMAGE"
    -e HOSTING_SERVER_FQDN="$(hostname -f 2>/dev/null || hostname)")
  local name
  while IFS= read -r name; do
    case "$name" in HOSTING_ROOT|HOSTING_IMAGE|HOSTING_IMAGE_OVERRIDE|HOSTING_VERSION|HOSTING_PULL|HOSTING_INSTALL_DOCKER) continue ;; esac
    args+=(-e "$name")
  done < <(env | grep -oE '^(HOSTING_[A-Z0-9_]+|ESTRICHONE_REGISTRY)=' | tr -d '=' || true)
  if interactive; then
    docker run -it "${args[@]}" "$IMAGE" "$@" < /dev/tty
  else
    docker run -i "${args[@]}" "$IMAGE" "$@" < /dev/null
  fi
}

# --- Datenverzeichnis -------------------------------------------------------------------------------------------
if [ -z "${HOSTING_ROOT:-}" ] && { [ "$COMMAND" = "install" ] || [ "$COMMAND" = "repair" ]; } && [ ! -f /srv/estrichone-hosting/.env ]; then
  ask HOSTING_ROOT "Datenverzeichnis (Instanzen, Sicherungen, Konsole)" "/srv/estrichone-hosting"
fi
ROOT="${HOSTING_ROOT:-/srv/estrichone-hosting}"
case "$ROOT" in /*) ;; *) fail "Das Datenverzeichnis muss ein absoluter Pfad sein." ;; esac

# --- Image ------------------------------------------------------------------------------------------------------
if [ "$COMMAND" = "update" ]; then
  IMAGE="$REGISTRY/estrichone-hosting:${1:-${HOSTING_VERSION:-latest}}"
  [ $# -gt 0 ] && shift
  COMMAND=install
elif [ -n "${HOSTING_IMAGE:-}" ] || [ -n "${HOSTING_VERSION:-}" ] || [ ! -f "$ROOT/.env" ]; then
  IMAGE="${HOSTING_IMAGE:-$REGISTRY/estrichone-hosting:${HOSTING_VERSION:-latest}}"
else
  # Reparatur/Verwaltung: bei der installierten Version bleiben.
  IMAGE="$(grep -E '^HOSTING_IMAGE=' "$ROOT/.env" | cut -d= -f2-)"
  IMAGE="${IMAGE:-$REGISTRY/estrichone-hosting:latest}"
fi

case "$COMMAND" in
  status)
    docker ps -a --filter name=estrichone-hosting --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'
    if [ -f "$ROOT/.env" ]; then
      port="$(grep -E '^HOSTING_CONSOLE_LOCAL_PORT=' "$ROOT/.env" | cut -d= -f2)"
      if curl -fsS "http://127.0.0.1:${port:-8099}/health" 2>/dev/null; then echo; else say "Konsole antwortet nicht auf http://127.0.0.1:${port:-8099}/health"; fi
      say "Instanzen: $(find "$ROOT/instances" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | wc -l) · Datenverzeichnis: $ROOT"
    else
      say "Keine Installation unter $ROOT gefunden."
    fi
    exit 0 ;;
  logs)
    exec docker logs -f --tail "${1:-200}" estrichone-hosting-console ;;
esac

[ "$COMMAND" = "install" ] || [ "$COMMAND" = "repair" ] || [ -f "$ROOT/.env" ] || fail "Keine Installation unter $ROOT gefunden."
mkdir -p "$ROOT"
chmod 755 "$ROOT"
pull_image

case "$COMMAND" in
  reset-admin) run_image reset-admin "$@"; exit 0 ;;
  uninstall)
    run_image uninstall "$@"
    rm -f "$CLI"
    case " $* " in *" --purge "*) rm -f "$CONF" ;; esac
    say "Befehl „estrichone-hosting“ entfernt. Neu installieren: curl -fsSL https://raw.githubusercontent.com/florians25/EstrichONE/main/hosting/install.sh | sudo bash"
    exit 0 ;;
esac

# --- Installieren / Reparieren / Aktualisieren ------------------------------------------------------------------
run_image install
# Diesen Befehl auf dem Server bereitstellen (aus dem Image, passend zur installierten Version).
if docker run --rm --entrypoint cat "$IMAGE" /opt/estrichone-hosting/estrichone-hosting > "$CLI.neu" 2>/dev/null && [ -s "$CLI.neu" ]; then
  chmod 755 "$CLI.neu" && mv "$CLI.neu" "$CLI"
else
  rm -f "$CLI.neu"
fi
umask 022
{
  echo "# EstrichONE Hosting-Konsole (von install.sh geschrieben)"
  echo "HOSTING_ROOT=$ROOT"
} > "$CONF"
say "Verwaltung: sudo estrichone-hosting [status|update|logs|reset-admin|uninstall]"
