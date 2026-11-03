#!/usr/bin/env bash
# Erneuert alle Handbuch-Bilder: frische Demo-Instanz (umgebung.sh), Ersteinrichtung im Browser, Demodaten, Bildschirmfotos.
# Voraussetzungen: Docker, .NET SDK, Node.js ≥ 22.12 mit `npm ci` in web/, Chromium (PLAYWRIGHT_CHROMIUM), ImageMagick.
# Aufruf: docs/handbuch/scripts/bilder.sh [bildname …]   danach: node docs/handbuch/scripts/pdf.mjs
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
export HB_WORK="${HB_WORK:-${TMPDIR:-/tmp}/estrichone-handbuch}"
"$HERE/umgebung.sh"
trap '"$HERE/umgebung.sh" stop' EXIT
node "$HERE/bilder.mjs" einrichtung
python3 "$HERE/demodaten.py" http://localhost:5191
node "$HERE/bilder.mjs" "$@"
