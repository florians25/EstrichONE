#!/usr/bin/env bash
# Erzeugt das Logosystem und alle Plattform-Symbole (Python 3 + fonttools + uharfbuzz, Chromium, ImageMagick).
#   pip install fonttools uharfbuzz
#   branding/build.sh            # alles
#   branding/build.sh --concepts # zusätzlich die Vergleichstafel concepts/overview.png
set -euo pipefail
cd "$(dirname "$0")"
root=..
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

python3 tools/final.py   # SVGs in branding/ + Android-Vektoren

./render.sh logo.svg "$tmp/round.png" 1024
./render.sh logo-small.svg "$tmp/small.png" 1024
./render.sh logo-square.svg "$tmp/square.png" 1024
./render.sh logo-mark.svg "$tmp/mark.png" 1024
# Groß: Lanczos aus 1024. Kleinform (auf 16-px-Raster gelegt): Box-Filter, damit Kanten pixelgenau bleiben.
big() { convert "$1" -filter Lanczos -resize "$2x$2" -strip "$3"; }
tiny() { convert "$tmp/small.png" -filter Box -resize "$1x$1" -strip "$2"; }
noalpha() { convert "$1" -filter Lanczos -resize "$2x$2" -background '#0B2D5B' -alpha remove -alpha off -strip "$3"; }

# Web
pub="$root/web/public"
noalpha "$tmp/square.png" 180 "$pub/apple-touch-icon.png"   # iOS rundet selbst ab
big "$tmp/round.png" 192 "$pub/icon-192.png"
big "$tmp/round.png" 512 "$pub/icon-512.png"
tiny 16 "$tmp/fav-16.png"; tiny 32 "$tmp/fav-32.png"; big "$tmp/round.png" 48 "$tmp/fav-48.png"
convert "$tmp/fav-16.png" "$tmp/fav-32.png" "$tmp/fav-48.png" "$pub/favicon.ico"
cp logo-small.svg "$pub/favicon.svg"
cp logo.svg logo-mark.svg logo-mark-dark.svg logo-mark-white.svg logo-mark-black.svg \
   logo-horizontal.svg logo-horizontal-white.svg logo-horizontal-mono-black.svg logo-horizontal-mono-white.svg "$pub/"

# Windows
assets="$root/desktop/src/EstrichOne.Desktop/Assets"
mkdir -p "$assets"
for s in 16 24 32; do tiny $s "$tmp/win-$s.png"; done
for s in 48 64 128 256; do big "$tmp/round.png" $s "$tmp/win-$s.png"; done
convert "$tmp"/win-{16,24,32,48,64,128,256}.png "$assets/EstrichONE.ico"
big "$tmp/mark.png" 64 "$assets/logo-mark.png"
big "$tmp/round.png" 256 "$assets/logo.png"

# iOS (eine Größe, Xcode erzeugt den Rest; ohne Transparenz)
noalpha "$tmp/square.png" 1024 "$root/ios/EstrichONE/Resources/Assets.xcassets/AppIcon.appiconset/AppIcon.png"

# Vorschau für Dokumentation und Release
mkdir -p png
big "$tmp/round.png" 512 png/logo-512.png
cp "$tmp/round.png" png/logo-1024.png
./render.sh logo-horizontal.svg png/logo-horizontal.png 1200 "$(python3 -c "
import re;w=float(re.search(r'viewBox=\"0 0 ([\d.]+)',open('logo-horizontal.svg').read()).group(1));print(round(1200*100/w))")"
./render.sh logo-horizontal-white.svg png/logo-horizontal-white.png 1200 "$(python3 -c "
import re;w=float(re.search(r'viewBox=\"0 0 ([\d.]+)',open('logo-horizontal-white.svg').read()).group(1));print(round(1200*100/w))")"
tools/presentation.sh

if [[ "${1:-}" == "--concepts" ]]; then tools/overview.sh; fi
echo "Symbole erzeugt."
