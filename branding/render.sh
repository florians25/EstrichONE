#!/usr/bin/env bash
# Rendert ein SVG mit Chromium (headless) als PNG: branding/render.sh <svg> <ausgabe.png> <größe>
# Kleine Größen (Symbole) besser aus 1024 px mit ImageMagick verkleinern (siehe build.sh).
set -euo pipefail
svg="$(realpath "$1")"; out="$(realpath -m "$2")"; size="$3"
chrome="${CHROME:-$(ls -d /opt/pw-browsers/chromium-*/chrome-linux/chrome 2>/dev/null | head -1)}"
work="$(mktemp -d)"
cat > "$work/page.html" <<HTML
<html><head><style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}img{display:block;width:${size}px;height:${size}px}</style></head>
<body><img src="file://$svg"></body></html>
HTML
"$chrome" --headless=new --no-sandbox --disable-gpu --hide-scrollbars --force-device-scale-factor=1 \
  --default-background-color=00000000 --window-size="$((size + 200)),$((size + 200))" --screenshot="$work/shot.png" \
  "file://$work/page.html" >/dev/null 2>&1
# Der sichtbare Bereich ist etwas kleiner als das Fenster: größer rendern und exakt zuschneiden.
convert "$work/shot.png" -crop "${size}x${size}+0+0" +repage "$out"
rm -rf "$work"
