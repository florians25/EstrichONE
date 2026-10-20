#!/usr/bin/env bash
# Präsentationsgrafik des endgültigen Logos: branding/png/presentation.png
#   branding/tools/presentation.sh   (wird von build.sh aufgerufen)
set -euo pipefail
cd "$(dirname "$0")/.."
b="$(pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
./render.sh logo.svg "$tmp/round.png" 1024
./render.sh logo-small.svg "$tmp/small.png" 1024
./render.sh logo-square.svg "$tmp/square.png" 1024
for s in 256 128 64 48; do convert "$tmp/round.png" -filter Lanczos -resize ${s}x$s "$tmp/r$s.png"; done
for s in 32 24 16; do convert "$tmp/small.png" -filter Box -resize ${s}x$s "$tmp/r$s.png"; done
convert "$tmp/square.png" -filter Lanczos -resize 180x180 "$tmp/sq180.png"
for f in logo.svg logo-small.svg logo-mark.svg logo-mark-dark.svg logo-mark-white.svg logo-mark-black.svg \
         logo-horizontal.svg logo-horizontal-white.svg logo-horizontal-mono-black.svg logo-horizontal-mono-white.svg; do
  cp "$f" "$tmp/"
done
python3 - "$tmp" <<'PY'
import sys
tmp = sys.argv[1]
themes = [  # (Name, Shell-Hintergrund, Seiten-Hintergrund, Shell-Text, Rand, Bildmarke, Seiten-Text)
  ("SAP Horizon", "#FFFFFF", "#F5F6F7", "#1D2D3E", "#D9D9D9", "logo.svg", "#1D2D3E"),
  ("SAP Horizon Dark", "#1D232A", "#12171C", "#EAECEE", "#2B323A", "logo.svg", "#EAECEE"),
  ("SAP Fiori 3", "#354A5F", "#F7F7F7", "#FFFFFF", "#354A5F", "logo.svg", "#32363A"),
  ("SAP Fiori 3 Dark", "#29313A", "#1C2228", "#FAFAFA", "#29313A", "logo.svg", "#FAFAFA"),
  ("SAP Belize", "#3F5161", "#EFF4F9", "#FFFFFF", "#3F5161", "logo.svg", "#333333"),
  ("High Contrast Black", "#000000", "#000000", "#FFFFFF", "#FFFFFF", "logo-mark-white.svg", "#FFFFFF"),
  ("High Contrast White", "#FFFFFF", "#FFFFFF", "#000000", "#000000", "logo-mark-black.svg", "#000000"),
]
shell = "".join(f'''<div class="theme" style="background:{pg};border-color:{bd}">
  <div class="shell" style="background:{sh};border-bottom:1px solid {bd}"><img src="{mark}" height="32"><b style="color:{tx}">EstrichONE</b>
  <span style="color:{tx}">Baustellen</span></div><div class="tn" style="color:{ptx}">{name}<small>{mark}</small></div></div>''' for name, sh, pg, tx, bd, mark, ptx in themes)
sizes = "".join(f'<figure><img src="r{s}.png" width="{s}"><figcaption>{s}</figcaption></figure>' for s in (256, 128, 64, 48, 32, 24, 16))
marks = "".join(f'<div class="mk" style="background:{bg}"><img src="{f}" height="120"><span style="color:{c}">{f}</span></div>' for f, bg, c in (
  ("logo-mark.svg", "#FFFFFF", "#556B82"), ("logo-mark-dark.svg", "#0B2D5B", "#8EC5FF"),
  ("logo-mark-white.svg", "#000000", "#FFFFFF"), ("logo-mark-black.svg", "#FFFFFF", "#000000")))
page = f'''<!doctype html><html><head><meta charset="utf-8"><style>
*{{box-sizing:border-box}} body{{margin:0;width:2400px;background:#EEF1F5;font-family:"DejaVu Sans",sans-serif;color:#1D2D3E}}
.hero{{display:grid;grid-template-columns:1fr 1fr;height:420px}} .hero div{{display:flex;align-items:center;justify-content:center}}
.hero img{{height:150px}} h3{{margin:0 0 18px;font-size:22px;color:#556B82;font-weight:normal;letter-spacing:.04em;text-transform:uppercase}}
.band{{padding:40px 64px}}
.themes{{display:grid;grid-template-columns:repeat(7,1fr);gap:18px}}
.theme{{border:1px solid;border-radius:12px;overflow:hidden;height:190px;position:relative}}
.shell{{height:56px;display:flex;align-items:center;gap:12px;padding:0 14px}} .shell b{{font-size:17px}} .shell span{{font-size:14px;opacity:.75;margin-left:auto}}
.tn{{position:absolute;left:16px;bottom:14px;font-size:16px;font-weight:bold}} .tn small{{display:block;font-weight:normal;opacity:.7;font-size:13px;margin-top:4px}}
.row{{display:grid;grid-template-columns:1.25fr 1fr;gap:40px}}
.sizes{{display:flex;align-items:flex-end;gap:34px;background:#fff;border-radius:16px;padding:32px;height:370px}}
figure{{margin:0;text-align:center}} figcaption{{font-size:14px;color:#7A8A9A;margin-top:10px}}
.marks{{display:grid;grid-template-columns:repeat(4,1fr);gap:14px}} .mk{{border-radius:16px;height:370px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:28px;border:1px solid #D9DEE4}}
.mk span{{font-size:14px}}
.ctx{{display:grid;grid-template-columns:1.2fr 1fr 1fr 1fr;gap:24px}}
.card{{border-radius:16px;height:380px;overflow:hidden;position:relative}}
.login{{background:linear-gradient(135deg,#174A8B,#0B2D5B);display:flex;align-items:center;justify-content:center}}
.login .box{{background:#fff;border-radius:14px;width:440px;padding:34px;box-shadow:0 18px 50px #0006}}
.login .box img{{height:46px}} .in{{height:38px;border:1px solid #C3CCD6;border-radius:8px;margin-top:16px}} .btn{{height:40px;border-radius:8px;background:#0B2D5B;margin-top:20px;color:#fff;display:flex;align-items:center;justify-content:center;font-size:15px}}
.phone{{background:linear-gradient(160deg,#7AA6D8,#2B4E7E);display:flex;gap:34px;align-items:center;justify-content:center}}
.app{{text-align:center;color:#fff;font-size:15px}} .app img{{display:block;margin-bottom:8px}}
.ios img{{width:120px;height:120px;border-radius:27px}} .andr img{{width:120px;height:120px;border-radius:50%}}
.win{{background:#DDE5EE;display:flex;flex-direction:column}} .titlebar{{height:44px;background:#F3F5F8;display:flex;align-items:center;gap:10px;padding:0 14px;font-size:14px}}
.taskbar{{margin-top:auto;height:58px;background:#E9EDF2EE;display:flex;align-items:center;justify-content:center;gap:16px;border-top:1px solid #C9D2DC}}
.tb{{width:44px;height:44px;border-radius:6px;display:flex;align-items:center;justify-content:center}} .tb.on{{background:#fff}}
.browser{{background:#DEE1E6;display:flex;flex-direction:column}} .tabs{{display:flex;padding:10px 10px 0}}
.tab{{background:#fff;border-radius:10px 10px 0 0;padding:10px 16px;display:flex;align-items:center;gap:10px;font-size:14px;width:260px}}
.page{{background:#fff;flex:1;display:flex;flex-direction:column}} .ub{{height:52px;border-bottom:1px solid #E5E9EE;display:flex;align-items:center;gap:12px;padding:0 16px}}
.cap{{font-size:14px;color:#7A8A9A;margin-top:10px}}
</style></head><body>
<div class="hero"><div style="background:#fff"><img src="logo-horizontal.svg"></div><div style="background:#0B2D5B"><img src="logo-horizontal-white.svg"></div></div>
<div class="band"><h3>Auf allen OpenUI5-Themes (ShellBar)</h3><div class="themes">{shell}</div></div>
<div class="band row"><div><h3>App-Symbol in Größen</h3><div class="sizes">{sizes}</div></div>
<div><h3>Bildmarke ohne Fläche</h3><div class="marks">{marks}</div></div></div>
<div class="band"><h3>Im Einsatz</h3><div class="ctx">
 <div><div class="card login"><div class="box"><img src="logo-horizontal.svg"><div class="in"></div><div class="in"></div><div class="btn">Anmelden</div></div></div><div class="cap">Anmeldeseite</div></div>
 <div><div class="card phone"><div class="app ios"><img src="sq180.png">EstrichONE</div><div class="app andr"><img src="sq180.png">EstrichONE</div></div><div class="cap">iOS und Android (Systemmaske)</div></div>
 <div><div class="card win"><div class="titlebar"><img src="r16.png" width="16">EstrichONE</div><div class="taskbar"><div class="tb"></div><div class="tb on"><img src="r32.png" width="32"></div><div class="tb"></div></div></div><div class="cap">Windows: Titelleiste 16 px, Taskleiste 32 px</div></div>
 <div><div class="card browser"><div class="tabs"><div class="tab"><img src="r16.png" width="16">EstrichONE – Baustellen</div></div><div class="page"><div class="ub"><img src="logo.svg" height="30"><b>EstrichONE</b></div></div></div><div class="cap">Browser: Favicon 16 px</div></div>
</div></div></body></html>'''
open(f"{tmp}/presentation.html", "w").write(page)
PY
mkdir -p png
./render.sh "$tmp/presentation.html" png/presentation.png 2400 1840
echo "png/presentation.png geschrieben."
