#!/bin/sh
# Prüft update-phases.sh ohne Docker (CI: infra.yml, lokal: sh docker/installer/test-update-phases.sh):
# Reihenfolge und Inhalt der Phasen, Mindestdauern (verkürzt), Standard 60 / 60 / 30 s, Fehlschlag mit und ohne laufendes System.
# shellcheck disable=SC1111,SC1112,SC2034,SC2015
set -eu
HERE="$(cd "$(dirname "$0")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
fail() { echo "FEHLER: $*" >&2; exit 1; }
field() { python3 -c 'import json,sys; v=json.load(open(sys.argv[1])).get(sys.argv[2]); print("" if v is None else v)' "$PHASE_FILE" "$1"; }

# shellcheck disable=SC1091
. "$HERE/update-phases.sh"
PHASE_FILE="$WORK/backup-status/update-maintenance.json"
PHASE_FROM="1.0.15" PHASE_TARGET="1.0.16" PHASE_SOURCE="test"

# Standard: 60 / 60 / 30 s, ungültige Angaben fallen auf den Standard zurück.
ESTRICHONE_UPDATE_ANNOUNCE_SECONDS="" ESTRICHONE_UPDATE_MIN_SECONDS="abc" ESTRICHONE_UPDATE_FINISH_SECONDS=""
phase_init
[ "$PHASE_ANNOUNCE" = 60 ] && [ "$PHASE_MIN" = 60 ] && [ "$PHASE_FINISH" = 30 ] || fail "Standarddauer nicht 60 / 60 / 30 s"

# Verkürzter Ablauf: 2 s Ankündigung, 3 s Update, 2 s Abschluss.
ESTRICHONE_UPDATE_ANNOUNCE_SECONDS=2 ESTRICHONE_UPDATE_MIN_SECONDS=3 ESTRICHONE_UPDATE_FINISH_SECONDS=2
phase_init
start="$(date +%s)"
phase_write announce "$PHASE_ANNOUNCE"
[ "$(field phase)" = announce ] || fail "Ankündigung fehlt"
[ "$(field targetVersion)" = 1.0.16 ] && [ "$(field fromVersion)" = 1.0.15 ] || fail "Versionen fehlen"
[ -n "$(field phaseEndsAt)" ] && [ -n "$(field expiresAt)" ] || fail "Ende/Ablauf fehlen"
[ "$(stat -c %a "$PHASE_FILE")" = 644 ] || fail "Datei nicht lesbar für API/Caddy"
phase_wait "$PHASE_ANNOUNCE" "Update beginnt in" > "$WORK/out"
grep -q "Update beginnt in 0:02" "$WORK/out" || fail "Countdown fehlt"
phase_update
[ "$(field phase)" = update ] || fail "Update-Phase fehlt"
phase_finish > "$WORK/out"
[ ! -e "$PHASE_FILE" ] || fail "Nach dem Abschluss ist die Wartung nicht beendet"
grep -q "Phase 3/3" "$WORK/out" || fail "Abschluss fehlt"
took=$(( $(date +%s) - start ))
[ "$took" -ge 7 ] || fail "Ablauf zu kurz: $took s (erwartet ≥ 7 s)"

# Fehlschlag, System läuft weiter: Status zeigen, dann freigeben.
phase_init
phase_write update 3
phase_abort "Version 1.0.16 startet nicht" 1 > "$WORK/out"
[ ! -e "$PHASE_FILE" ] || fail "Nach Fehlschlag mit laufendem System nicht freigegeben"
# Fehlschlag, System läuft nicht: gesperrt lassen, Meldung im Stand.
phase_init
phase_write update 3
phase_abort "Datenbank startet nicht" 0 > "$WORK/out"
[ "$(field phase)" = failed ] && [ "$(field message)" = "Datenbank startet nicht" ] || fail "Fehlschlag nicht im Stand"
grep -q -- "--wartung-beenden" "$WORK/out" || fail "Hinweis zum Freigeben fehlt"
echo "update-phases.sh: alle Prüfungen bestanden ($took s)."
