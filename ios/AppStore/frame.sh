#!/bin/bash
# App-Store-Screenshots rahmen: Markenhintergrund (Nachtblau), Überschrift und Unterzeile, Aufnahme mit Geräterahmen.
# Erzeugt aus den Rohaufnahmen des CI-Jobs „App-Store-Screenshots“ (iPhone 6,9" 1320×2868, iPad 13" 2064×2752)
# alle Größen für App Store Connect, jeweils roh und gerahmt, in exakt Apples Pixelmaßen:
#   iphone-6.9 (1320×2868), iphone-6.5 (1284×2778, aus 6,9" skaliert), ipad-13 (2064×2752).
#
#   ios/AppStore/frame.sh <rohordner mit iphone69-<motiv>-hell.png und ipad13-<motiv>-hell.png> [zielordner]
#
# Braucht ImageMagick (6 oder 7) und die Schrift Inter Display (OFL); Pfade über FONT_BOLD/FONT_MEDIUM änderbar.
set -euo pipefail

IN=$1
OUT=${2:-"$(dirname "$0")/screenshots"}
IM=${IM:-$(command -v magick || command -v convert)}
BOLD=${FONT_BOLD:-/usr/share/fonts/opentype/inter/InterDisplay-Bold.otf}
MEDIUM=${FONT_MEDIUM:-/usr/share/fonts/opentype/inter/InterDisplay-Medium.otf}
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

SCENES=(heute bauakte bereich-fotos disposition dispo-freigabe erstbegehung meldung-detail kundenkarte)
TITLES=("Ihr Tag auf einen Blick" "Die Bauakte immer dabei" "Fotos und Scans, sauber abgelegt" "Wer fährt wohin"
        "Planen und freigeben – unterwegs" "Erstbegehung und Abnahme digital" "Hilfe direkt aus der App" "Kunden griffbereit")
SUBS=("Einsatz, Route und Aufgaben – auch offline" "Fotos, Bautagebuch, Mängel und Protokolle" "Platzsparend gespeichert und trotzdem scharf"
      "Kolonnen, Fahrzeuge und Baustellen in Fahrtreihenfolge" "Zuweisen, tauschen, freigeben – mit Konfliktprüfung"
      "Prüfpunkte, Fotos und Unterschrift als PDF" "Problem melden, Antworten im Verlauf" "Anrufen, schreiben, Route starten")

# frame <roh.png> <breite> <höhe> <titel> <unterzeile> <ziel.png> <eckradius-anteil>
frame() {
  local src=$1 w=$2 h=$3 title=$4 sub=$5 out=$6 corner=$7
  local base sw sh srcw srch top title_pt sub_pt radius bezel
  read -r srcw srch < <("$IM" "$src" -format '%w %h\n' info:)
  base=$(( w < h * 46 / 100 ? w : h * 46 / 100 ))
  title_pt=$(( base * 70 / 1000 ))
  sub_pt=$(( base * 36 / 1000 ))
  top=$(( base * 31 / 100 ))
  sh=$(( h - top - base * 4 / 100 ))
  sw=$(( sh * srcw / srch ))
  if [ "$sw" -gt $(( w * 86 / 100 )) ]; then sw=$(( w * 86 / 100 )); sh=$(( sw * srch / srcw )); fi
  radius=$(( sw * corner / 1000 ))
  bezel=$(( sw * 16 / 1000 ))
  # Aufnahme mit runden Ecken, dunklem Rahmen und weichem Schatten.
  "$IM" "$src" -resize "${sw}x${sh}!" "$TMP/shot.png"
  "$IM" -size "${sw}x${sh}" xc:none -fill white -draw "roundrectangle 0,0,$((sw - 1)),$((sh - 1)),$radius,$radius" "$TMP/mask.png"
  "$IM" "$TMP/shot.png" "$TMP/mask.png" -alpha off -compose CopyOpacity -composite "$TMP/rounded.png"
  "$IM" -size "$((sw + 2 * bezel))x$((sh + 2 * bezel))" xc:none -fill '#0A0F1A' \
    -draw "roundrectangle 0,0,$((sw + 2 * bezel - 1)),$((sh + 2 * bezel - 1)),$((radius + bezel)),$((radius + bezel))" \
    "$TMP/rounded.png" -geometry "+$bezel+$bezel" -compose over -composite "$TMP/device.png"
  "$IM" "$TMP/device.png" \( +clone -background '#000814' -shadow "45x$((base / 40))+0+$((base / 60))" \) +swap \
    -background none -layers merge +repage "$TMP/device-shadow.png"
  # Hintergrund mit Verlauf, Texte zentriert, Gerät unten.
  # Texte passen sich der Breite an (höchstens 90 %), nie abgeschnitten.
  "$IM" -background none -fill white -font "$BOLD" -size "$(( w * 90 / 100 ))x$(( title_pt * 125 / 100 ))" -gravity center label:"$title" "$TMP/title.png"
  "$IM" -background none -fill '#CFE5FF' -font "$MEDIUM" -size "$(( w * 90 / 100 ))x$(( sub_pt * 135 / 100 ))" -gravity center label:"$sub" "$TMP/sub.png"
  "$IM" -size "${w}x${h}" gradient:'#0B2E66'-'#061536' \
    "$TMP/title.png" -gravity north -geometry "+0+$(( base * 8 / 100 ))" -compose over -composite \
    "$TMP/sub.png" -gravity north -geometry "+0+$(( base * 8 / 100 + title_pt * 135 / 100 ))" -compose over -composite \
    "$TMP/device-shadow.png" -gravity north -geometry "+0+$(( top - bezel ))" -compose over -composite \
    -alpha off -strip "PNG24:$out"
}

mkdir -p "$OUT"/{iphone-6.9,iphone-6.5,ipad-13}/roh
for index in "${!SCENES[@]}"; do
  scene=${SCENES[$index]}
  number=$(printf '%02d' $((index + 1)))
  phone="$IN/iphone69-$scene-hell.png"
  pad="$IN/ipad13-$scene-hell.png"
  if [ -f "$phone" ]; then
    "$IM" "$phone" -resize '1320x2868!' -alpha off -strip "PNG24:$OUT/iphone-6.9/roh/$number-$scene.png"
    # 6,5": gleiche Breite anteilig (1284×2790), oben und unten je 6 px beschnitten.
    "$IM" "$phone" -resize '1284x2790!' -gravity center -crop '1284x2778+0+0' +repage -alpha off -strip "PNG24:$OUT/iphone-6.5/roh/$number-$scene.png"
    frame "$phone" 1320 2868 "${TITLES[$index]}" "${SUBS[$index]}" "$OUT/iphone-6.9/$number-$scene.png" 70
    frame "$phone" 1284 2778 "${TITLES[$index]}" "${SUBS[$index]}" "$OUT/iphone-6.5/$number-$scene.png" 70
  fi
  if [ -f "$pad" ]; then
    "$IM" "$pad" -resize '2064x2752!' -alpha off -strip "PNG24:$OUT/ipad-13/roh/$number-$scene.png"
    frame "$pad" 2064 2752 "${TITLES[$index]}" "${SUBS[$index]}" "$OUT/ipad-13/$number-$scene.png" 28
  fi
done

# Prüfung: exakte Pixelmaße, kein Alphakanal.
status=0
for file in "$OUT"/*/*.png "$OUT"/*/roh/*.png; do
  [ -f "$file" ] || continue
  read -r fw fh alpha < <("$IM" "$file" -format '%w %h %A\n' info:)
  case "$file" in
    */iphone-6.9/*) want="1320 2868" ;;
    */iphone-6.5/*) want="1284 2778" ;;
    */ipad-13/*) want="2064 2752" ;;
  esac
  if [ "$fw $fh" != "$want" ] || [ "$alpha" != "False" ] && [ "$alpha" != "Undefined" ]; then
    echo "FALSCH: $file ($fw×$fh, Alpha $alpha, erwartet ${want/ /×})"
    status=1
  fi
done
echo "Fertig: $(find "$OUT" -name '*.png' | wc -l) Bilder in $OUT"
exit $status
