#!/usr/bin/env bash
# Bilder des App-Handbuchs aus echten Simulator-Aufnahmen der iOS-App (Demomodus) erzeugen.
#
#   docs/handbuch/app/bilder.sh <ordner>
#
# <ordner> enthält die Aufnahmen des CI-Jobs „Screenshots“ (Workflow „iOS“, Artefakte bzw. Protokollzeilen SHOT|…,
# siehe ios/README.md) als iphone-<bildschirm>-hell.png und ipad-<bildschirm>-hell.png. Die hochauflösenden Motive
# „Heute“ und iPad kommen aus dem App-Store-Paket (ios/AppStore/roh). Ausgabe: docs/handbuch/app/bilder/*.jpg,
# iPhone 540 px breit, iPad 1200 px breit, JPEG Qualität 84 (ImageMagick).
set -euo pipefail
src="${1:?Ordner mit iphone-…-hell.png / ipad-…-hell.png angeben}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../../.." && pwd)"
out="$here/bilder"
mkdir -p "$out"

iphone=(start start-adresse anmelden anmelden-code warten gesperrt sicherheit sitzung-beendet mehr mehr-zuruecksetzen
  zuruecksetzen heute-unten heute-gross heute-chat aufgaben woche disposition dispo-zuweisen dispo-verschieben dispo-tauschen
  dispo-fahrzeug dispo-kolonnen dispo-freigabe baustellen bauakte bauakte-mitte bauakte-unten bereich-fotos bereich-aufgaben
  bereich-protokolle bereich-tagebuch bereich-raeume bereich-basisdaten bereich-kontakte bereich-notizen raum protokoll
  erstbegehung abnahme tagebuch-neu mangel-neu kontakt-neu basisdaten-neu erfassen neu-1 neu-2 neu-3 neu-4 kunden kundenkarte
  chat chat-verlauf chat-gruppe chat-anhang chat-diktat meldung meldung-detail meldung-bearbeitung postfach sync stoerung wartung)
ipad=(heute-quer bauakte-quer kunden-quer disposition-quer)

conv() { convert "$1" -resize "$2>" -strip -sampling-factor 4:2:0 -quality 84 -interlace JPEG "$3"; }
for s in "${iphone[@]}"; do conv "$src/iphone-$s-hell.png" 540x "$out/iphone-$s.jpg"; done
for s in "${ipad[@]}"; do conv "$src/ipad-$s-hell.png" 1200x "$out/ipad-$s.jpg"; done
roh="$root/ios/AppStore/roh"
conv "$roh/iphone-6.9/01-heute.jpg" 540x "$out/iphone-heute.jpg"
conv "$roh/iphone-6.9/01-heute.jpg" 700x "$out/titel.jpg"
for s in 01-heute 02-bauakte 04-disposition 07-chat-verlauf; do conv "$roh/ipad-13/$s.jpg" 1200x "$out/ipad-${s#*-}.jpg"; done
du -sh "$out"
