#!/usr/bin/env bash
# Erzeugt alle App-Symbole aus logo.svg / logo-square.svg / logo-mark.svg (Chromium + ImageMagick).
#   branding/build.sh
set -euo pipefail
cd "$(dirname "$0")"
root=..
tmp="$(mktemp -d)"
./render.sh logo.svg "$tmp/round.png" 1024
./render.sh logo-square.svg "$tmp/square.png" 1024
./render.sh logo-mark.svg "$tmp/mark.png" 1024
small() { convert "$1" -filter Lanczos -resize "$2x$2" -strip "$3"; }

# Web
small "$tmp/round.png" 180 "$root/web/public/apple-touch-icon.png"
small "$tmp/round.png" 192 "$root/web/public/icon-192.png"
small "$tmp/round.png" 512 "$root/web/public/icon-512.png"
for s in 16 32 48; do small "$tmp/round.png" $s "$tmp/fav-$s.png"; done
convert "$tmp/fav-16.png" "$tmp/fav-32.png" "$tmp/fav-48.png" "$root/web/public/favicon.ico"
cp logo.svg "$root/web/public/favicon.svg"

# Windows
assets="$root/desktop/src/EstrichOne.Desktop/Assets"
mkdir -p "$assets"
for s in 16 24 32 48 64 128 256; do small "$tmp/round.png" $s "$tmp/win-$s.png"; done
convert "$tmp"/win-{16,24,32,48,64,128,256}.png "$assets/EstrichONE.ico"
small "$tmp/mark.png" 64 "$assets/logo-mark.png"
small "$tmp/round.png" 256 "$assets/logo.png"

# iOS (eine Größe, Xcode erzeugt den Rest; ohne Transparenz)
convert "$tmp/square.png" -background '#0B2D5B' -alpha remove -alpha off -strip \
  "$root/ios/EstrichONE/Resources/Assets.xcassets/AppIcon.appiconset/AppIcon.png"

# Vorschau für Dokumentation und Release
mkdir -p png
small "$tmp/round.png" 512 png/logo-512.png
small "$tmp/round.png" 1024 png/logo-1024.png
rm -rf "$tmp"
echo "Symbole erzeugt."
