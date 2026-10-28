#!/bin/bash
# App-Store-Bilder erzeugen und prüfen. Aus den Rohaufnahmen des CI-Jobs „App-Store-Screenshots“
# (iPhone 6,9" 1320×2868, iPad 13" 2064×2752; Dateien iphone69-<motiv>-hell.png und ipad13-<motiv>-hell.png) entstehen:
#
#   upload/iphone-6.9/NN-<motiv>.jpg   1320×2868  gerahmt mit Überschrift – so in App Store Connect hochladen
#   upload/iphone-6.5/NN-<motiv>.jpg   1284×2778  gerahmt (6,5"-Fach, optional; Apple skaliert sonst 6,9")
#   upload/ipad-13/NN-<motiv>.jpg      2064×2752  gerahmt
#   roh/<gerät>/NN-<motiv>.jpg         dieselben Größen ohne Rahmen (Alternative, ebenfalls zulässig)
#   banner-1920x886/NN-<motiv>.jpg     1920×886 Querformat (iPad + iPhone, Überschrift) für Web/Werbung, NICHT App Store
#
#   ios/AppStore/frame.sh <rohordner> [zielordner, Standard ios/AppStore]
#   ios/AppStore/frame.sh --pruefen [zielordner]     nur prüfen
#
# Alle Bilder: JPEG Qualität 94 ohne Farbunterabtastung, sRGB mit eingebettetem Profil (sRGB.icc), kein Alphakanal.
# Die Prüfung am Ende (Pixelmaß, Farbraum, Alpha, Anzahl 1–10 je Gerät) bricht mit Fehlercode ab, wenn etwas nicht passt.
# Braucht ImageMagick (6 oder 7), Python 3 mit Pillow und NumPy (grip.py) sowie die Schrift Inter Display (OFL); Pfade über FONT_BOLD/FONT_MEDIUM änderbar.
set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
IM=${IM:-$(command -v magick || command -v convert)}
BOLD=${FONT_BOLD:-/usr/share/fonts/opentype/inter/InterDisplay-Bold.otf}
MEDIUM=${FONT_MEDIUM:-/usr/share/fonts/opentype/inter/InterDisplay-Medium.otf}
ICC="$HERE/sRGB.icc"
JPEG=(-strip -profile "$ICC" -quality 94 -sampling-factor 4:4:4 -type TrueColor)

# Motive in Upload-Reihenfolge (höchstens 10). Die ersten drei sieht man in der Suche.
SCENES=(heute bauakte bereich-fotos disposition dispo-zuweisen erstbegehung chat-verlauf kundenkarte meldung-detail)
TITLES=("Ihr Tag auf einen Blick" "Die Bauakte immer dabei" "Fotos und Scans, sauber abgelegt" "Wer fährt wohin"
        "Disponieren in Sekunden" "Erstbegehung und Abnahme digital" "Kurzer Draht ins Büro" "Kunden griffbereit"
        "Hilfe direkt aus der App")
SUBS=("Einsatz, Route und Aufgaben – auch offline" "Fotos, Bautagebuch, Mängel und Protokolle"
      "Beschriftet, als PDF gescannt, sofort im Büro" "Kolonnen, Fahrzeuge und Baustellen in Fahrtreihenfolge"
      "Zuweisen, verschieben, tauschen – mit Konfliktprüfung" "Prüfpunkte, Fotos und Unterschrift als PDF"
      "Chat mit Büro und Kolonne – nur in Ihrem Betrieb" "Anrufen, schreiben, Route starten"
      "Problem melden, Antworten im Verlauf")

check() {
  local out=$1 status=0 dir want n file fw fh alpha space
  for dir in upload/iphone-6.9 upload/iphone-6.5 upload/ipad-13 roh/iphone-6.9 roh/iphone-6.5 roh/ipad-13 banner-1920x886; do
    case "$dir" in
      */iphone-6.9) want="1320 2868" ;;
      */iphone-6.5) want="1284 2778" ;;
      */ipad-13) want="2064 2752" ;;
      banner-*) want="1920 886" ;;
    esac
    n=$(find "$out/$dir" -maxdepth 1 -name '*.jpg' 2>/dev/null | wc -l)
    if [ "$n" -lt 1 ] || [ "$n" -gt 10 ]; then echo "FALSCH: $dir enthält $n Bilder (1–10 erlaubt)"; status=1; fi
    for file in "$out/$dir"/*.jpg; do
      [ -f "$file" ] || continue
      read -r fw fh alpha space < <("$IM" "$file" -format '%w %h %A %[colorspace]\n' info:)
      icc=$("$IM" "$file" -format '%[icc:description]' info: 2>/dev/null || true)
      if [ "$fw $fh" != "$want" ] || { [ "$alpha" != "False" ] && [ "$alpha" != "Undefined" ]; } || [ "$space" != "sRGB" ] \
        || [[ "$icc" != *sRGB* ]]; then
        echo "FALSCH: $file ($fw×$fh, Alpha $alpha, $space, Profil '$icc'; erwartet ${want/ /×}, sRGB, ohne Alpha)"
        status=1
      fi
    done
    printf '  %-20s %2d Bilder  %s\n' "$dir" "$n" "${want/ /×}"
  done
  return $status
}

if [ "${1:-}" = "--pruefen" ]; then
  check "${2:-$HERE}" && echo "Alle Bilder in Ordnung."
  exit
fi

IN=$1
OUT=${2:-$HERE}
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

# Rohaufnahme nach sRGB (Simulator-Aufnahmen können Display P3 tragen), ohne Alpha.
srgb() {
  if [ -n "$("$IM" "$1" -format '%[icc:description]' info: 2>/dev/null || true)" ]; then
    "$IM" "$1" -profile "$ICC" -alpha off "$2"
  else
    "$IM" "$1" -alpha off "$2"
  fi
}

# iPad: Fenstergriff von iPadOS 26 (unten rechts) entfernen – der Simulator zeigt ihn immer, auch mit UIRequiresFullScreen.
clean_grip() { python3 -I "$HERE/grip.py" "$1"; }

# device <aufnahme> <breite> <eckradius-promille> <ziel.png>: Aufnahme mit runden Ecken, schmalem dunklem Rahmen, Schatten.
device() {
  local src=$1 sw=$2 corner=$3 out=$4 srcw srch sh radius bezel blur
  read -r srcw srch < <("$IM" "$src" -format '%w %h\n' info:)
  sh=$(( sw * srch / srcw ))
  radius=$(( sw * corner / 1000 ))
  bezel=$(( sw * 18 / 1000 ))
  blur=$(( sw / 28 ))
  "$IM" "$src" -resize "${sw}x${sh}!" "$TMP/shot.png"
  "$IM" -size "${sw}x${sh}" xc:none -fill white -draw "roundrectangle 0,0,$((sw - 1)),$((sh - 1)),$radius,$radius" "$TMP/mask.png"
  "$IM" "$TMP/shot.png" "$TMP/mask.png" -alpha off -compose CopyOpacity -composite "$TMP/rounded.png"
  "$IM" -size "$((sw + 2 * bezel))x$((sh + 2 * bezel))" xc:none -fill '#0A0F1A' \
    -draw "roundrectangle 0,0,$((sw + 2 * bezel - 1)),$((sh + 2 * bezel - 1)),$((radius + bezel)),$((radius + bezel))" \
    -fill none -stroke '#3A4A66' -strokewidth 2 \
    -draw "roundrectangle 1,1,$((sw + 2 * bezel - 2)),$((sh + 2 * bezel - 2)),$((radius + bezel)),$((radius + bezel))" \
    "$TMP/rounded.png" -geometry "+$bezel+$bezel" -compose over -composite "$TMP/dev.png"
  "$IM" "$TMP/dev.png" \( +clone -background '#000510' -shadow "55x${blur}+0+$((blur / 2))" \) +swap \
    -background none -layers merge +repage "$out"
}

# text <schrift> <punkt> <farbe> <höchstbreite> <text> <ziel.png>: einzeilig; zu lang → kleiner, nie abgeschnitten.
text() {
  "$IM" -background none -fill "$3" -font "$1" -pointsize "$2" label:"$5" -trim +repage "$6"
  if [ "$("$IM" "$6" -format '%w' info:)" -gt "$4" ]; then
    "$IM" -background none -fill "$3" -font "$1" -size "${4}x" label:"$5" -trim +repage "$6"
  fi
}

# Markenhintergrund: Verlauf Nachtblau (#0B2E66 → #061536) mit weichem Lichtschein in Markenblau.
backdrop() {
  local w=$1 h=$2 gx=$3 gy=$4 gr=$5 out=$6
  "$IM" -size "${w}x${h}" gradient:'#0B2E66'-'#061536' \
    \( -size "$((gr * 2))x$((gr * 2))" radial-gradient:'#0062CC'-none -channel A -evaluate multiply 0.55 +channel \) \
    -geometry "+$((gx - gr))+$((gy - gr))" -compose over -composite "$out"
}

# portrait <aufnahme> <breite> <höhe> <titel> <unterzeile> <ziel.jpg> <eckradius-promille>
portrait() {
  local src=$1 w=$2 h=$3 title=$4 sub=$5 out=$6 corner=$7 base title_pt sub_pt top sw sh srcw srch th
  read -r srcw srch < <("$IM" "$src" -format '%w %h\n' info:)
  base=$(( w < h * 46 / 100 ? w : h * 46 / 100 ))
  title_pt=$(( base * 74 / 1000 ))
  sub_pt=$(( base * 36 / 1000 ))
  text "$BOLD" "$title_pt" white "$(( w * 88 / 100 ))" "$title" "$TMP/title.png"
  text "$MEDIUM" "$sub_pt" '#BFD9FF' "$(( w * 88 / 100 ))" "$sub" "$TMP/sub.png"
  th=$("$IM" "$TMP/title.png" -format '%h' info:)
  top=$(( base * 30 / 100 ))
  sh=$(( h - top - base * 5 / 100 ))
  sw=$(( sh * srcw / srch ))
  if [ "$sw" -gt $(( w * 84 / 100 )) ]; then sw=$(( w * 84 / 100 )); fi
  device "$src" "$sw" "$corner" "$TMP/device.png"
  # Gerät samt Rahmen und Schatten muss vollständig ins Bild passen (unten etwas Luft).
  local maxdh=$(( h - top + base / 60 - base * 4 / 100 )) dh
  dh=$("$IM" "$TMP/device.png" -format '%h' info:)
  if [ "$dh" -gt "$maxdh" ]; then "$IM" "$TMP/device.png" -resize "x$maxdh" "$TMP/device.png"; fi
  backdrop "$w" "$h" $(( w / 2 )) $(( h * 55 / 100 )) $(( w * 7 / 10 )) "$TMP/bg.png"
  "$IM" "$TMP/bg.png" \
    "$TMP/title.png" -gravity north -geometry "+0+$(( base * 9 / 100 ))" -compose over -composite \
    "$TMP/sub.png" -gravity north -geometry "+0+$(( base * 9 / 100 + th + base * 25 / 1000 ))" -compose over -composite \
    "$TMP/device.png" -gravity north -geometry "+0+$(( top - base / 60 ))" -compose over -composite \
    -alpha off -colorspace sRGB "${JPEG[@]}" "$out"
}

# banner <ipad> <iphone> <titel> <unterzeile> <ziel.jpg>: 1920×886, Text links, iPad und iPhone rechts.
banner() {
  local pad=$1 phone=$2 title=$3 sub=$4 out=$5 w=1920 h=886
  text "$BOLD" 64 white 820 "$title" "$TMP/title.png"
  text "$MEDIUM" 32 '#BFD9FF' 820 "$sub" "$TMP/sub.png"
  "$IM" -background none -fill '#5FA8FF' -font "$BOLD" -pointsize 26 -kerning 3 label:"ESTRICHONE" -trim +repage "$TMP/brand.png"
  local th; th=$("$IM" "$TMP/title.png" -format '%h' info:)
  backdrop "$w" "$h" 1420 470 820 "$TMP/bg.png"
  "$IM" "$TMP/bg.png" "$TMP/brand.png" -gravity northwest -geometry "+120+$(( 443 - th / 2 - 90 ))" -compose over -composite \
    "$TMP/title.png" -gravity northwest -geometry "+120+$(( 443 - th / 2 - 20 ))" -compose over -composite \
    "$TMP/sub.png" -gravity northwest -geometry "+122+$(( 443 + th / 2 + 4 ))" -compose over -composite "$TMP/bg2.png"
  if [ -f "$pad" ]; then
    device "$pad" 560 28 "$TMP/devpad.png"
    "$IM" "$TMP/bg2.png" "$TMP/devpad.png" -gravity northwest -geometry "+1270+58" -compose over -composite "$TMP/bg2.png"
  fi
  if [ -f "$phone" ]; then
    device "$phone" 330 70 "$TMP/devphone.png"
    "$IM" "$TMP/bg2.png" "$TMP/devphone.png" -gravity northwest -geometry "+$([ -f "$pad" ] && echo 1040 || echo 1300)+96" \
      -compose over -composite "$TMP/bg2.png"
  fi
  "$IM" "$TMP/bg2.png" -crop "${w}x${h}+0+0" +repage -alpha off -colorspace sRGB "${JPEG[@]}" "$out"
}

rm -rf "$OUT/upload" "$OUT/roh" "$OUT/banner-1920x886"
mkdir -p "$OUT"/upload/{iphone-6.9,iphone-6.5,ipad-13} "$OUT"/roh/{iphone-6.9,iphone-6.5,ipad-13} "$OUT/banner-1920x886"
for index in "${!SCENES[@]}"; do
  scene=${SCENES[$index]}
  number=$(printf '%02d' $((index + 1)))
  phone="$TMP/phone-$scene.png"
  pad="$TMP/pad-$scene.png"
  rm -f "$phone" "$pad"
  [ -f "$IN/iphone69-$scene-hell.png" ] && srgb "$IN/iphone69-$scene-hell.png" "$phone"
  if [ -f "$IN/ipad13-$scene-hell.png" ]; then srgb "$IN/ipad13-$scene-hell.png" "$pad"; clean_grip "$pad"; fi
  if [ -f "$phone" ]; then
    "$IM" "$phone" -resize '1320x2868!' "${JPEG[@]}" "$OUT/roh/iphone-6.9/$number-$scene.jpg"
    # 6,5": anteilig auf 1284 breit (1284×2790), oben und unten je 6 px beschnitten.
    "$IM" "$phone" -resize '1284x2790!' -gravity center -crop '1284x2778+0+0' +repage "${JPEG[@]}" "$OUT/roh/iphone-6.5/$number-$scene.jpg"
    portrait "$phone" 1320 2868 "${TITLES[$index]}" "${SUBS[$index]}" "$OUT/upload/iphone-6.9/$number-$scene.jpg" 70
    portrait "$phone" 1284 2778 "${TITLES[$index]}" "${SUBS[$index]}" "$OUT/upload/iphone-6.5/$number-$scene.jpg" 70
  fi
  if [ -f "$pad" ]; then
    "$IM" "$pad" -resize '2064x2752!' "${JPEG[@]}" "$OUT/roh/ipad-13/$number-$scene.jpg"
    portrait "$pad" 2064 2752 "${TITLES[$index]}" "${SUBS[$index]}" "$OUT/upload/ipad-13/$number-$scene.jpg" 28
  fi
  if [ -f "$phone" ] || [ -f "$pad" ]; then
    banner "$pad" "$phone" "${TITLES[$index]}" "${SUBS[$index]}" "$OUT/banner-1920x886/$number-$scene.jpg"
  fi
  echo "  $number $scene"
done

check "$OUT"
echo "Fertig: $(find "$OUT/upload" "$OUT/roh" "$OUT/banner-1920x886" -name '*.jpg' | wc -l) Bilder in $OUT"
