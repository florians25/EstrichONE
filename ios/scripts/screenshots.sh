#!/bin/bash
# Screenshots der App im Demomodus (Simulator), hell und dunkel. Die App zeigt die Bildschirme selbst nacheinander
# (Screenshot-Modus, nur Debug-Builds: EstrichONE/Support/ScreenshotMode.swift) und meldet über eine Datei in ihrem
# Datencontainer, wann ein Bildschirm steht; das Skript nimmt ihn auf und gibt das Zeichen zum Weitermachen.
#
#   ios/scripts/screenshots.sh <simulator-udid> <präfix> <bildschirme,kommagetrennt> <zielordner> [dunkel: 1|0]
set -euo pipefail

UDID=$1
PREFIX=$2
SCENES=$3
OUT=$4
DARK=${5:-1}
BUNDLE=de.estrichone.EstrichONE

mkdir -p "$OUT"
xcrun simctl ui "$UDID" appearance light
DATA=$(xcrun simctl get_app_container "$UDID" "$BUNDLE" data)
STATE="$DATA/Documents/shot-state"
NEXT="$DATA/Documents/shot-next"
mkdir -p "$DATA/Documents"
rm -f "$STATE" "$NEXT"

xcrun simctl launch --terminate-running-process "$UDID" "$BUNDLE" \
  -EstrichShots "$SCENES" -AppleLanguages "(de)" -AppleLocale de_DE >/dev/null

start=$(date +%s)
count=0
while true; do
  state=$(cat "$STATE" 2>/dev/null || true)
  case "$state" in
    ready:*)
      name=${state#ready:}
      rm -f "$STATE"
      xcrun simctl io "$UDID" screenshot --type=png "$OUT/$PREFIX-$name-hell.png" >/dev/null 2>&1
      if [ "$DARK" = "1" ]; then
        xcrun simctl ui "$UDID" appearance dark
        sleep 1.2
        xcrun simctl io "$UDID" screenshot --type=png "$OUT/$PREFIX-$name-dunkel.png" >/dev/null 2>&1
        xcrun simctl ui "$UDID" appearance light
        sleep 0.4
      fi
      count=$((count + 1))
      echo "  $PREFIX: $name ($(( $(date +%s) - start )) s)"
      touch "$NEXT"
      ;;
    done)
      break
      ;;
  esac
  if [ $(( $(date +%s) - start )) -gt 600 ]; then
    echo "::warning::Screenshot-Modus ($PREFIX) hat nach 10 Minuten nicht abgeschlossen (Stand: ${state:-leer})."
    break
  fi
  sleep 0.2
done
xcrun simctl terminate "$UDID" "$BUNDLE" >/dev/null 2>&1 || true
echo "$PREFIX: $count Bildschirme in $(( $(date +%s) - start )) s"
