# EstrichONE – geordneter Update-Ablauf (deploy/README.md, „Update-Ablauf“), eingebunden von update.sh und install.sh.
#
#   1. Ankündigung (Standard 60 s): Alle angemeldeten Benutzer (Web und Apps) sehen einen Countdown „Update auf Version X
#      beginnt in 0:59“, die Apps gleichen vorher ab. Neue Anmeldungen sind gesperrt.
#   2. Update (mindestens 60 s): Die API beendet alle Sitzungen und sperrt alles; Sicherung und Austausch der Container.
#      Web und Apps zeigen den Wartungsbildschirm – auch, solange die API nicht läuft (Caddy liefert /update-status.json).
#   3. Abschluss (Standard 60 s): Neue Version läuft und ist bereit (/health/ready); danach Freigabe, Clients laden neu.
#
# Der Stand liegt in data/backup-status/update-maintenance.json (API liest ihn, Caddy liefert ihn als /update-status.json).
# Dauer je Phase in Sekunden (Umgebung oder .env): ESTRICHONE_UPDATE_ANNOUNCE_SECONDS, ESTRICHONE_UPDATE_MIN_SECONDS,
# ESTRICHONE_UPDATE_FINISH_SECONDS – Standard je 60 (zusammen mindestens 3 Minuten); kürzer nur für Tests.
#
# Erwartet vor phase_init: PHASE_FILE (Pfad der Datei), PHASE_FROM, PHASE_TARGET, PHASE_SOURCE.
# shellcheck shell=sh disable=SC1111,SC1112

phase_iso() { date -u -d "@$1" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u +%Y-%m-%dT%H:%M:%SZ; }
phase_now() { date -u +%s; }
phase_json() { printf '"%s"' "$(printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g' | tr -d '\n\r')"; }
phase_seconds() { # phase_seconds WERT STANDARD – nur ganze Zahlen ≥ 0
  case "${1:-}" in ""|*[!0-9]*) echo "$2" ;; *) echo "$1" ;; esac
}

phase_init() {
  PHASE_ANNOUNCE="$(phase_seconds "${ESTRICHONE_UPDATE_ANNOUNCE_SECONDS:-}" 60)"
  PHASE_MIN="$(phase_seconds "${ESTRICHONE_UPDATE_MIN_SECONDS:-}" 60)"
  PHASE_FINISH="$(phase_seconds "${ESTRICHONE_UPDATE_FINISH_SECONDS:-}" 60)"
  PHASE_STARTED="$(phase_now)"
  PHASE_ID="$(date -u +%Y%m%dT%H%M%SZ)-${PHASE_TARGET:-update}"
  PHASE_UPDATE_STARTED=""
  PHASE_ACTIVE=0
}

# phase_write PHASE [SEKUNDEN_BIS_ENDE] [MELDUNG] – atomar, für API (uid 1654) und Caddy lesbar.
phase_write() {
  now="$(phase_now)"
  ends=null
  [ -n "${2:-}" ] && ends="\"$(phase_iso $((now + $2)))\""
  # Verwaist (abgebrochenes Skript): nach 6 Stunden ignoriert die API den Stand.
  expires="$(phase_iso $((now + 21600)))"
  dir="$(dirname "$PHASE_FILE")"
  [ -d "$dir" ] || mkdir -p "$dir"
  printf '{"id":%s,"phase":"%s","fromVersion":%s,"targetVersion":%s,"startedAt":"%s","phaseStartedAt":"%s","phaseEndsAt":%s,"expiresAt":"%s","message":%s,"source":%s}\n' \
    "$(phase_json "$PHASE_ID")" "$1" "$(phase_json "${PHASE_FROM:-}")" "$(phase_json "${PHASE_TARGET:-}")" "$(phase_iso "$PHASE_STARTED")" \
    "$(phase_iso "$now")" "$ends" "$expires" "$( [ -n "${3:-}" ] && phase_json "$3" || echo null)" "$(phase_json "${PHASE_SOURCE:-update.sh}")" \
    > "$PHASE_FILE.tmp" && chmod 644 "$PHASE_FILE.tmp" && mv "$PHASE_FILE.tmp" "$PHASE_FILE"
  PHASE_ACTIVE=1
}

phase_clear() {
  rm -f "$PHASE_FILE" "$PHASE_FILE.tmp"
  PHASE_ACTIVE=0
}

# phase_wait SEKUNDEN TEXT – Countdown im Terminal (alle 10 s bzw. jede Sekunde in den letzten 5).
phase_wait() {
  left="$1"
  while [ "$left" -gt 0 ]; do
    if [ $((left % 10)) -eq 0 ] || [ "$left" -le 5 ]; then
      printf '  %s %d:%02d\n' "$2" $((left / 60)) $((left % 60))
    fi
    sleep 1
    left=$((left - 1))
  done
}

# 1. Ankündigung – Benutzer speichern, Apps gleichen ab; danach beginnt das Update.
phase_announce() {
  echo "Phase 1/3 – Ankündigung: Benutzer werden informiert (${PHASE_ANNOUNCE} s), neue Anmeldungen sind gesperrt."
  phase_write announce "$PHASE_ANNOUNCE"
  phase_wait "$PHASE_ANNOUNCE" "Update beginnt in"
}

# 2. Update – Sitzungen enden (die API prüft alle 2 s), Wartungsbildschirm bleibt bis zum Abschluss.
phase_update() {
  echo "Phase 2/3 – Update: Alle Sitzungen werden beendet, EstrichONE zeigt den Wartungsbildschirm."
  PHASE_UPDATE_STARTED="$(phase_now)"
  phase_write update "$PHASE_MIN"
  sleep 3
}

# 3. Abschluss – frühestens nach der Mindestdauer des Updates; danach Freigabe.
phase_finish() {
  if [ -n "$PHASE_UPDATE_STARTED" ]; then
    rest=$((PHASE_UPDATE_STARTED + PHASE_MIN - $(phase_now)))
    [ "$rest" -gt 0 ] && phase_wait "$rest" "Update-Phase noch"
  fi
  echo "Phase 3/3 – Abschluss: Neue Version läuft, Freigabe in ${PHASE_FINISH} s."
  phase_write finish "$PHASE_FINISH"
  phase_wait "$PHASE_FINISH" "Freigabe in"
  phase_clear
  echo "EstrichONE ist wieder freigegeben (Dauer insgesamt $(( $(phase_now) - PHASE_STARTED )) s)."
}

# Fehlschlag: läuft der bisherige bzw. neue Stand (gesund), Status kurz zeigen und freigeben; sonst gesperrt lassen.
# phase_abort MELDUNG GESUND(1/0)
phase_abort() {
  [ "${PHASE_ACTIVE:-0}" = "1" ] || return 0
  if [ "$2" = "1" ]; then
    echo "Update fehlgeschlagen – EstrichONE läuft weiter. Hinweis für die Benutzer, Freigabe in ${PHASE_FINISH} s."
    phase_write failed "$PHASE_FINISH" "$1"
    phase_wait "$PHASE_FINISH" "Freigabe in"
    phase_clear
  else
    phase_write failed "" "$1"
    echo "Der Wartungsbildschirm bleibt aktiv, bis das Problem behoben ist. Freigeben: sudo /opt/estrichone/update.sh --wartung-beenden"
    PHASE_ACTIVE=0
  fi
}
