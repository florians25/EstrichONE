#!/bin/bash
# Screenshots der App im Demomodus (Simulator), hell und dunkel. Die App zeigt die Bildschirme selbst nacheinander
# (Screenshot-Modus, nur Debug-Builds: EstrichONE/Support/ScreenshotMode.swift) und meldet über eine Datei in ihrem
# Datencontainer, wann ein Bildschirm steht; das Skript nimmt ihn auf und gibt das Zeichen zum Weitermachen.
# Stürzt die App ab oder hängt ein Bildschirm, wird das gemeldet (mit Absturzbericht) und mit dem nächsten weitergemacht.
#
#   ios/scripts/screenshots.sh <simulator-udid> <präfix> <bildschirme,kommagetrennt> <zielordner> [dunkel: 1|0]
set -uo pipefail

UDID=$1
PREFIX=$2
SCENES=$3
OUT=$4
DARK=${5:-1}
BUNDLE=de.estrichone.EstrichONE
SCENE_TIMEOUT=45
# Obergrenze je Gerät, damit die CI auch bei mehreren hängenden Bildschirmen zügig fertig wird.
TOTAL_TIMEOUT=540

mkdir -p "$OUT"
xcrun simctl ui "$UDID" appearance light
DATA=$(xcrun simctl get_app_container "$UDID" "$BUNDLE" data)
STATE="$DATA/Documents/shot-state"
NEXT="$DATA/Documents/shot-next"
mkdir -p "$DATA/Documents"

running() { pgrep -f "EstrichONE.app/EstrichONE" >/dev/null 2>&1; }

crash_report() {
  local report
  # shellcheck disable=SC2012 # Dateinamen ohne Sonderzeichen; neuester Bericht zuerst.
  report=$(ls -t "$HOME"/Library/Logs/DiagnosticReports/EstrichONE* 2>/dev/null | head -1)
  if [ -n "$report" ]; then
    echo "::group::Absturzbericht $(basename "$report")"
    head -c 12000 "$report"
    echo
    echo "::endgroup::"
  fi
}

# Startet die App mit den noch fehlenden Bildschirmen.
launch() {
  rm -f "$STATE" "$NEXT"
  # APPSTORE=1: Aufnahmen für den App Store ohne Demo-Kennzeichnung.
  xcrun simctl launch --terminate-running-process "$UDID" "$BUNDLE" \
    -EstrichShots "$1" -EstrichAppStore "$( [ "${APPSTORE:-0}" = "1" ] && echo YES || echo NO )" \
    -AppleLanguages "(de)" -AppleLocale de_DE >/dev/null
}

IFS=',' read -r -a LIST <<< "$SCENES"
start=$(date +%s)
count=0
index=0
launch "$SCENES"
waiting_since=$(date +%s)
while [ $index -lt ${#LIST[@]} ]; do
  if [ $(( $(date +%s) - start )) -gt $TOTAL_TIMEOUT ]; then
    echo "::warning::$PREFIX: nach $TOTAL_TIMEOUT s abgebrochen – ${LIST[$index]} und folgende fehlen."
    break
  fi
  state=$(cat "$STATE" 2>/dev/null || true)
  case "$state" in
    ready:*)
      name=${state#ready:}
      rm -f "$STATE"
      # „…-gross“: mit sehr großer Schrift (Bedienungshilfen) aufnehmen.
      if [[ "$name" == *-gross ]]; then
        xcrun simctl ui "$UDID" content_size accessibility-large >/dev/null 2>&1
        sleep 2
      fi
      xcrun simctl io "$UDID" screenshot --type=png "$OUT/$PREFIX-$name-hell.png" >/dev/null 2>&1
      if [ "$DARK" = "1" ]; then
        xcrun simctl ui "$UDID" appearance dark
        sleep 1.2
        xcrun simctl io "$UDID" screenshot --type=png "$OUT/$PREFIX-$name-dunkel.png" >/dev/null 2>&1
        xcrun simctl ui "$UDID" appearance light
        sleep 0.4
      fi
      if [[ "$name" == *-gross ]]; then
        xcrun simctl ui "$UDID" content_size large >/dev/null 2>&1
        sleep 1
      fi
      count=$((count + 1))
      echo "  $PREFIX: $name ($(( $(date +%s) - start )) s)"
      # Weiter in der Liste bis hinter diesen Bildschirm.
      while [ $index -lt ${#LIST[@]} ] && [ "${LIST[$index]}" != "$name" ]; do index=$((index + 1)); done
      index=$((index + 1))
      touch "$NEXT"
      waiting_since=$(date +%s)
      ;;
    done)
      break
      ;;
  esac
  if [ $index -lt ${#LIST[@]} ] && { ! running || [ $(( $(date +%s) - waiting_since )) -gt $SCENE_TIMEOUT ]; }; then
    scene=${LIST[$index]}
    if running; then
      echo "::warning::$PREFIX: Bildschirm '$scene' wurde nicht fertig (Zeitüberschreitung) – weiter mit dem nächsten."
      xcrun simctl io "$UDID" screenshot --type=png "$OUT/$PREFIX-$scene-haengt.png" >/dev/null 2>&1
    else
      echo "::warning::$PREFIX: Die App ist bei '$scene' abgestürzt – weiter mit dem nächsten."
      crash_report
    fi
    index=$((index + 1))
    if [ $index -lt ${#LIST[@]} ]; then
      rest=$(IFS=','; echo "${LIST[*]:$index}")
      launch "$rest"
      waiting_since=$(date +%s)
    fi
  fi
  sleep 0.2
done
xcrun simctl terminate "$UDID" "$BUNDLE" >/dev/null 2>&1 || true
echo "$PREFIX: $count Bildschirme in $(( $(date +%s) - start )) s"
