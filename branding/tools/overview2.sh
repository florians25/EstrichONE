#!/usr/bin/env bash
# Vergleichstafel der zweiten Konzeptrunde: branding/concepts/overview-v2.png
set -euo pipefail
cd "$(dirname "$0")/.."
python3 tools/concepts2.py
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
for k in a b c d e f; do
  ./render.sh "concepts/v2/$k-icon.svg" "$tmp/$k-1024.png" 1024
  for s in 16 32 64 140; do convert "$tmp/$k-1024.png" -filter Lanczos -resize ${s}x$s "$tmp/$k-$s.png"; done
  cp concepts/v2/$k-*.svg "$tmp/"
done
python3 - "$tmp" <<'PY'
import sys, html
tmp = sys.argv[1]
R = [
 ("a", "A · Nivellierwelle", "Drei Lagen Fließestrich: oben noch in Bewegung, unten exakt eben.", (3, 4, 5, 3),
  "Erzählt den Kern des Fließestrichs am schönsten. Bei 16 px aber nah am Menü-Symbol (drei Striche). Verworfen.", 0),
 ("b", "B · Schicht-Eins", "Die „1“ aus drei versetzten Lagen (Decke, Dämmung, Estrich) mit Tiefe.", (4, 3, 3, 4),
  "Kräftig, aber Ziffer-Logos sind verbreitet (1Password u. a.); die Lagen lesen sich eher als Schatten. Verworfen.", 0),
 ("c", "C · E1-Monogramm", "Der Rücken des E trägt den Anstrich der „1“ – Estrich und ONE in einem Zeichen; der Mittelstrich ist die Libelle.", (5, 5, 4, 5),
  "Eigenständig und sofort merkbar, liest sich als E und als 1 und bleibt bei 16 px klar. Ruhig genug für SAP-/Microsoft-Umfeld, als App-Icon mit Tiefe premium. → Auswahl.", 1),
 ("d", "D · Fugenraster", "Das E aus acht Estrichfeldern mit Scheinfugen; das fehlende Feld ist die Blase.", (4, 1, 3, 4),
  "Sauber modular, aber im Kachelraster sieht es aus wie das App-Übersicht-Symbol (Launcher). Verworfen.", 0),
 ("e", "E · Libelle", "Die Wasserwaage auf ihr Wesen reduziert: Kapsel, Blase, zwei Marken.", (4, 2, 5, 3),
  "Schöne Reduktion, wird aber als Umschalter (Toggle) gelesen – in einer Software-Oberfläche fatal. Verworfen.", 0),
 ("f", "F · Glas-E", "Plastisches E aus gegossenem Material, Kante in Markenblau.", (3, 2, 3, 2),
  "Wirkt nach Effekt-Typografie, die Kante zerfällt klein zu Rauschen, auf hellem Grund ohne Kachel kaum tragfähig. Verworfen.", 0),
]
L = ("Erkennbar 16 px", "Eigenständig", "Bezug Estrich/Bau", "Enterprise")
dots = lambda n: "".join(f'<i class="{"on" if i < n else ""}"></i>' for i in range(5))
rows = "".join(f'''<section class="{'win' if w else ''}"><div><h2>{n}</h2>{'<em>Auswahl</em>' if w else ''}<p>{html.escape(i)}</p></div>
<div class="lk l"><img src="{k}-lockup.svg"></div><div class="lk d"><img src="{k}-lockup-dark.svg"></div>
<div class="mk"><img src="{k}-mark.svg" width="70"></div>
<div class="sz"><img src="{k}-140.png" width="140"><img src="{k}-64.png" width="64"><img src="{k}-32.png" width="32"><img src="{k}-16.png" width="16"><img class="z" src="{k}-16.png" width="64"></div>
<div class="ev">{"".join(f'<div class="sc"><span>{a}</span><b>{dots(b)}</b></div>' for a, b in zip(L, s))}<p>{html.escape(v)}</p></div></section>''' for k, n, i, s, v, w in R)
open(f"{tmp}/o.html", "w").write(f'''<!doctype html><html><head><meta charset="utf-8"><style>
*{{box-sizing:border-box}}body{{margin:0;width:2600px;background:#F2F4F7;font-family:"DejaVu Sans",sans-serif;color:#0B1B33}}
header{{padding:44px 56px 20px}}h1{{margin:0;font-size:40px}}header p{{margin:8px 0 0;color:#556B82;font-size:20px}}
section{{display:grid;grid-template-columns:300px 470px 470px 130px 420px 1fr;gap:20px;align-items:center;margin:0 40px 18px;padding:20px;background:#fff;border-radius:18px;border:2px solid #E3E8EE}}
section.win{{border-color:#0062CC;box-shadow:0 0 0 5px #0062CC22}}h2{{margin:0 0 6px;font-size:24px}}
em{{font-style:normal;background:#0062CC;color:#fff;font-weight:bold;padding:4px 12px;border-radius:99px;font-size:14px}}
section p{{font-size:15px;line-height:1.45;color:#475E75}}.lk{{height:170px;border-radius:12px;display:flex;align-items:center;justify-content:center}}
.lk img{{height:76px}}.l{{border:1px solid #E3E8EE}}.d{{background:#0E1116}}.mk{{display:flex;justify-content:center}}
.sz{{display:flex;gap:16px;align-items:center}}.z{{image-rendering:pixelated;border:1px dashed #C3CCD6}}
.sc{{display:flex;justify-content:space-between;font-size:15px;padding:2px 0;max-width:400px}}
.sc i{{display:inline-block;width:13px;height:13px;border-radius:50%;background:#E3E8EE;margin-left:5px}}.sc i.on{{background:#0B1B33}}.win .sc i.on{{background:#0062CC}}
</style></head><body><header><h1>EstrichONE – Logo-Konzepte, Runde 2</h1>
<p>Sechs Richtungen, Markenblau #0062CC auf Nachtblau. Je Lockup hell/dunkel (Wortmarke in Pfaden), Bildmarke ohne Fläche, App-Icon 140/64/32/16 px. Bewertung 1–5.</p></header>{rows}</body></html>''')
PY
./render.sh "$tmp/o.html" concepts/overview-v2.png 2600 1560
echo "concepts/overview-v2.png geschrieben."
