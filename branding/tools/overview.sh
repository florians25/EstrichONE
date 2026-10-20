#!/usr/bin/env bash
# Vergleichstafel der Konzepte: branding/concepts/overview.png
#   branding/tools/overview.sh
set -euo pipefail
cd "$(dirname "$0")/.."
python3 tools/concepts.py
tmp="$(mktemp -d)"
for k in a b c d; do
  ./render.sh "concepts/concept-$k-icon.svg" "$tmp/$k-1024.png" 1024
  for s in 16 32 64 160; do convert "$tmp/$k-1024.png" -filter Lanczos -resize ${s}x$s "$tmp/$k-$s.png"; done
  cp "concepts/concept-$k-lockup.svg" "concepts/concept-$k-lockup-dark.svg" "$tmp/"
done
python3 - "$tmp" <<'PY'
import sys, html
tmp = sys.argv[1]
rows = [
  ("a", "A · Libellen-E", "Ein „E“ aus dem Estrichaufbau: oben die abgezogene Oberfläche, unten die Rohdecke, "
   "der Mittelstrich ist die Libelle einer Wasserwaage – eben und belegreif.",
   [("Erkennbar 16 px", 5), ("Eigenständig", 4), ("Bezug Estrich/Bau", 5), ("Enterprise", 5)],
   "Klarer Monogramm-Buchstabe, bleibt bei 16 px ein E. Die Libelle erzählt „eben“ ohne Klischee. "
   "Ruhig, symmetrisch, passt neben Microsoft-/SAP-Symbole. → Favorit, wird verfeinert.", True),
  ("b", "B · Pegel-O", "Das O von ONE als Schauglas: Fließestrich steht exakt waagrecht, darüber die Blase.",
   [("Erkennbar 16 px", 4), ("Eigenständig", 2), ("Bezug Estrich/Bau", 3), ("Enterprise", 3)],
   "Kräftige Form, aber liest sich als Bullauge/Waschmaschine; „ONE“ trägt kein E. Verworfen.", False),
  ("c", "C · Fugenplatte", "Die Kachel ist eine Estrichplatte; zwei Scheinfugen schneiden das E heraus.",
   [("Erkennbar 16 px", 3), ("Eigenständig", 4), ("Bezug Estrich/Bau", 4), ("Enterprise", 2)],
   "Kluge Idee, aber die Silhouette bricht aus dem App-Icon-Raster aus und wirkt angeschnitten; "
   "auf dunklen Flächen unruhig. Verworfen.", False),
  ("d", "D · Aufbau isometrisch", "Drei Lagen eines Fußbodenaufbaus in Isometrie.",
   [("Erkennbar 16 px", 2), ("Eigenständig", 1), ("Bezug Estrich/Bau", 3), ("Enterprise", 4)],
   "Sieht aus wie das generische „Ebenen“-Symbol aus Karten- und Grafik-Apps; ab 32 px verschwimmen die Lagen. Verworfen.", False),
]
def dots(n): return "".join(f'<i class="{"on" if i < n else ""}"></i>' for i in range(5))
out = []
for k, name, idea, scores, verdict, win in rows:
    sc = "".join(f'<div class="sc"><span>{l}</span><b>{dots(v)}</b></div>' for l, v in scores)
    out.append(f'''<section class="{'win' if win else ''}">
 <div class="head"><h2>{name}</h2>{'<em>Auswahl</em>' if win else ''}<p>{html.escape(idea)}</p></div>
 <div class="lock light"><img src="concept-{k}-lockup.svg"></div>
 <div class="lock dark"><img src="concept-{k}-lockup-dark.svg"></div>
 <div class="sizes"><img src="{k}-160.png" width="160"><div class="small">
   <img src="{k}-64.png" width="64"><img src="{k}-32.png" width="32"><img src="{k}-16.png" width="16">
   <img class="zoom" src="{k}-16.png" width="64"></div><div class="lbl">512 · 64 · 32 · 16 · 16 (vergrößert)</div>
   <div class="small dk"><img src="{k}-32.png" width="32"><img src="{k}-16.png" width="16"></div></div>
 <div class="eval">{sc}<p>{html.escape(verdict)}</p></div>
</section>''')
page = f'''<!doctype html><html><head><meta charset="utf-8"><style>
*{{box-sizing:border-box}} body{{margin:0;width:2400px;background:#F5F6F7;font-family:"DejaVu Sans",sans-serif;color:#1D2D3E}}
header{{padding:48px 64px 24px}} header h1{{margin:0;font-size:40px}} header p{{margin:8px 0 0;color:#556B82;font-size:20px}}
section{{display:grid;grid-template-columns:330px 560px 560px 420px 1fr;gap:24px;align-items:center;margin:0 48px 24px;padding:24px;background:#fff;border-radius:16px;border:2px solid #E5E9EE}}
section.win{{border-color:#3CCB7F;box-shadow:0 0 0 4px #3CCB7F33}}
h2{{margin:0 0 8px;font-size:26px}} em{{display:inline-block;font-style:normal;background:#3CCB7F;color:#0B2D5B;font-weight:bold;padding:4px 12px;border-radius:999px;font-size:15px}}
.head p{{font-size:16px;line-height:1.45;color:#475E75}}
.lock{{height:200px;border-radius:12px;display:flex;align-items:center;justify-content:center}} .lock img{{height:90px}}
.light{{background:#fff;border:1px solid #E5E9EE}} .dark{{background:#12171C}}
.sizes{{display:grid;grid-template-columns:160px 1fr;gap:6px 18px;align-items:center}}
.small{{display:flex;gap:14px;align-items:center}} .zoom{{image-rendering:pixelated;border:1px dashed #C3CCD6}}
.lbl{{grid-column:1/3;font-size:13px;color:#7A8A9A}} .dk{{grid-column:2;background:#12171C;padding:8px 12px;border-radius:8px;width:max-content}}
.sc{{display:flex;justify-content:space-between;font-size:16px;padding:3px 0;max-width:420px}}
.sc i{{display:inline-block;width:14px;height:14px;border-radius:50%;background:#E5E9EE;margin-left:5px}} .sc i.on{{background:#0B2D5B}}
.win .sc i.on{{background:#3CCB7F}} .eval p{{font-size:16px;line-height:1.45;color:#475E75}}
</style></head><body><header><h1>EstrichONE – Logo-Konzepte</h1>
<p>Vier Richtungen im selben 100er-Raster, je mit Wortmarke (Schrift in Pfade umgewandelt), hell/dunkel und in 512/64/32/16 px. Bewertung 1–5.</p></header>
{"".join(out)}</body></html>'''
open(f"{tmp}/overview.html", "w").write(page)
PY
./render.sh "$tmp/overview.html" concepts/overview.png 2400 1420
rm -rf "$tmp"
echo "concepts/overview.png geschrieben."
