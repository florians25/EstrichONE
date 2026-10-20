"""Bausteine für die EstrichONE-Logos: Formen und Wortmarke als reine SVG-Pfade.

Die Wortmarke wird mit HarfBuzz (Kerning) gesetzt und mit fontTools in Pfade umgewandelt.
Die Schriften liegen unter branding/fonts (SIL Open Font License), damit das Ergebnis
nicht von installierten Systemschriften abhängt.
"""
from __future__ import annotations

import io
import math
from functools import lru_cache
from pathlib import Path

import uharfbuzz as hb
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

FONTS = Path(__file__).resolve().parent.parent / "fonts"


def num(v: float) -> str:
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s


# ---------------------------------------------------------------- Formen

def squircle(x: float, y: float, size: float, n: float = 5.0, steps: int = 48) -> str:
    """Superellipse |x|^n + |y|^n = 1 (kontinuierliche Ecken wie bei Systemsymbolen)."""
    cx, cy, r = x + size / 2, y + size / 2, size / 2
    pts = []
    for i in range(steps * 4):
        t = 2 * math.pi * i / (steps * 4)
        c, s = math.cos(t), math.sin(t)
        px = cx + r * math.copysign(abs(c) ** (2 / n), c)
        py = cy + r * math.copysign(abs(s) ** (2 / n), s)
        pts.append((px, py))
    return "M" + " L".join(f"{num(px)} {num(py)}" for px, py in pts) + "Z"


def rrect(x: float, y: float, w: float, h: float, r: float | tuple = 0) -> str:
    """Rechteck mit Eckradien (r oder (oben links, oben rechts, unten rechts, unten links))."""
    tl, tr, br, bl = (r, r, r, r) if not isinstance(r, tuple) else r
    p = [f"M{num(x + tl)} {num(y)}", f"H{num(x + w - tr)}"]
    if tr: p.append(f"A{num(tr)} {num(tr)} 0 0 1 {num(x + w)} {num(y + tr)}")
    p.append(f"V{num(y + h - br)}")
    if br: p.append(f"A{num(br)} {num(br)} 0 0 1 {num(x + w - br)} {num(y + h)}")
    p.append(f"H{num(x + bl)}")
    if bl: p.append(f"A{num(bl)} {num(bl)} 0 0 1 {num(x)} {num(y + h - bl)}")
    p.append(f"V{num(y + tl)}")
    if tl: p.append(f"A{num(tl)} {num(tl)} 0 0 1 {num(x + tl)} {num(y)}")
    return "".join(p) + "Z"


def circle(cx: float, cy: float, r: float) -> str:
    return (f"M{num(cx - r)} {num(cy)}A{num(r)} {num(r)} 0 1 0 {num(cx + r)} {num(cy)}"
            f"A{num(r)} {num(r)} 0 1 0 {num(cx - r)} {num(cy)}Z")


# ---------------------------------------------------------------- Schrift

@lru_cache(maxsize=None)
def _font(name: str):
    path = FONTS / name
    tt = TTFont(path)
    tt.flavor = None
    buf = io.BytesIO()
    tt.save(buf)
    data = buf.getvalue()
    tt = TTFont(io.BytesIO(data))
    face = hb.Face(data)
    return tt, hb.Font(face), face.upem


def font_metrics(name: str) -> dict:
    tt, _, upem = _font(name)
    os2 = tt["OS/2"]
    return {"upem": upem, "cap": os2.sCapHeight, "x": os2.sxHeight}


def text_path(runs: list[tuple[str, str, str]], cap_height: float, x: float = 0, baseline: float = 0,
              tracking: float = 0.0) -> tuple[list[tuple[str, str]], float]:
    """Setzt Textläufe [(text, schriftdatei, farbe)] als Pfade.

    cap_height: gewünschte Versalhöhe in SVG-Einheiten (alle Läufe auf dieselbe Versalhöhe skaliert).
    tracking: Laufweite in 1/1000 em. Gibt [(farbe, d)] und die Endposition x zurück.
    """
    out = []
    pen_x = x
    for text, name, color in runs:
        tt, font, upem = _font(name)
        scale = cap_height / tt["OS/2"].sCapHeight
        buf = hb.Buffer()
        buf.add_str(text)
        buf.guess_segment_properties()
        hb.shape(font, buf, {"kern": True, "liga": False})
        glyph_set = tt.getGlyphSet()
        order = tt.getGlyphOrder()
        d = []
        for info, pos in zip(buf.glyph_infos, buf.glyph_positions):
            gname = order[info.codepoint]
            pen = SVGPathPen(glyph_set, ntos=num)
            tp = TransformPen(pen, (scale, 0, 0, -scale, pen_x + pos.x_offset * scale, baseline - pos.y_offset * scale))
            glyph_set[gname].draw(tp)
            d.append(pen.getCommands())
            pen_x += pos.x_advance * scale + tracking / 1000 * upem * scale
        pen_x -= tracking / 1000 * upem * scale  # keine Laufweite nach dem letzten Zeichen
        out.append((color, "".join(d)))
        # Abstand zwischen Läufen wie normale Laufweite
        pen_x += tracking / 1000 * upem * scale
    pen_x -= tracking / 1000 * _font(runs[-1][1])[2] * (cap_height / _font(runs[-1][1])[0]["OS/2"].sCapHeight)
    return out, pen_x


def svg(view_w: float, view_h: float, body: str, label: str = "EstrichONE", defs: str = "", comment: str = "") -> str:
    head = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {num(view_w)} {num(view_h)}" role="img" aria-label="{label}">\n'
    if comment:
        head += f"  <!-- {comment} -->\n"
    if defs:
        head += f"  <defs>{defs}</defs>\n"
    return head + body + "</svg>\n"
