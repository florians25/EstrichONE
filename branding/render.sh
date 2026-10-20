#!/usr/bin/env bash
# Rendert ein SVG oder eine HTML-Seite mit Chromium (headless) als PNG:
#   branding/render.sh <svg|html> <ausgabe.png> <breite> [höhe]
# Ohne Höhe wird quadratisch gerendert. Kleine Größen (Symbole) besser aus 1024 px mit ImageMagick
# verkleinern (siehe build.sh).
set -euo pipefail
src="$(realpath "$1")"; out="$(realpath -m "$2")"; w="$3"; h="${4:-$3}"
chrome="${CHROME:-$(ls -d /opt/pw-browsers/chromium-*/chrome-linux/chrome 2>/dev/null | head -1)}"
work="$(mktemp -d)"
if [[ "$src" == *.html ]]; then
  page="$src"
else
  page="$work/page.html"
  cat > "$page" <<HTML
<html><head><style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}img{display:block;width:${w}px;height:${h}px}</style></head>
<body><img src="file://$src"></body></html>
HTML
fi
"$chrome" --headless=new --no-sandbox --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
  --allow-file-access-from-files --default-background-color=00000000 \
  --window-size="$((w + 200)),$((h + 200))" --screenshot="$work/shot.png" \
  "file://$page" >/dev/null 2>&1
# Der sichtbare Bereich ist etwas kleiner als das Fenster: größer rendern und exakt zuschneiden.
convert "$work/shot.png" -crop "${w}x${h}+0+0" +repage "$out"
rm -rf "$work"
